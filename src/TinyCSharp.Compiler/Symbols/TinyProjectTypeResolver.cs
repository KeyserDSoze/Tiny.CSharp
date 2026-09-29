using System.Text;
using Microsoft.CodeAnalysis;
using TinyCSharp.Compiler.Compilation;
using TinyCSharp.Compiler.Decompilation;
using TinyCSharp.Compiler.Diagnostics;
using TinyCSharp.Compiler.Language;
using TinyCSharp.Compiler.Parsing;

namespace TinyCSharp.Compiler.Symbols;

public sealed class TinyProjectTypeResolver
{
    private const string TinyStubPrefix = "__tiny_stub__/";

    public async Task<IReadOnlyList<TinyDiagnostic>> ResolveAsync(
        string projectDirectory,
        IReadOnlyList<TinySyntaxTree> documents,
        CancellationToken cancellationToken = default)
    {
        if (documents.Count == 0)
        {
            return Array.Empty<TinyDiagnostic>();
        }

        var sourceDocuments = new List<CSharpSourceDocument>();
        var tinyStubPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < documents.Count; i++)
        {
            var document = documents[i];
            var stubPath = $"{TinyStubPrefix}{i}-{document.ClassName}.cs";

            tinyStubPaths.Add(stubPath);
            sourceDocuments.Add(new CSharpSourceDocument(
                stubPath,
                CreateDeclarationStub(document)));
        }

        foreach (var csharpPath in DiscoverHandwrittenCSharp(projectDirectory))
        {
            var source = await File.ReadAllTextAsync(csharpPath, cancellationToken);
            sourceDocuments.Add(new CSharpSourceDocument(csharpPath, source));
        }

        var semanticCompilation = CSharpSemanticCompilation.Create(sourceDocuments);
        var index = BuildTypeIndex(
            semanticCompilation.Compilation,
            tinyStubPaths);
        var diagnostics = new List<TinyDiagnostic>();

        foreach (var document in documents)
        {
            foreach (var property in document.Properties)
            {
                property.Type = ResolveType(
                    property.Type,
                    property,
                    document,
                    index,
                    diagnostics);
            }
        }

        return diagnostics;
    }

    private static TinyType ResolveType(
        TinyType type,
        TinyProperty property,
        TinySyntaxTree document,
        IReadOnlyDictionary<TypeKey, IReadOnlyList<TypeCandidate>> index,
        List<TinyDiagnostic> diagnostics)
    {
        var resolvedArguments = type.TypeArguments
            .Select(argument => ResolveType(
                argument,
                property,
                document,
                index,
                diagnostics))
            .ToArray();

        type = type with { TypeArguments = resolvedArguments };

        if (TinyLanguage.GetCanonicalTypeToken(type.Name) != type.Name ||
            type.Name.Contains('.', StringComparison.Ordinal))
        {
            return type;
        }

        var key = new TypeKey(type.Name, type.TypeArguments.Count);

        if (!index.TryGetValue(key, out var candidates) ||
            candidates.Count == 0)
        {
            diagnostics.Add(new TinyDiagnostic(
                TinyDiagnosticSeverity.Warning,
                $"Type '{type.Name}' could not be resolved by Tiny.CSharp. " +
                "The type name will be emitted unchanged and validated by the C# compiler.",
                document.SourceFilePath,
                property.TypeLine,
                property.TypeColumn,
                Code: TinyDiagnosticCodes.UnresolvedType));

            return type;
        }

        var orderedCandidates = candidates
            .OrderBy(candidate =>
                string.Equals(
                    candidate.Namespace,
                    document.Namespace,
                    StringComparison.Ordinal)
                    ? 0
                    : 1)
            .ThenBy(candidate =>
                document.Usings.Contains(
                    candidate.Namespace,
                    StringComparer.Ordinal)
                    ? 0
                    : 1)
            .ThenBy(candidate => candidate.SourceKind switch
            {
                TinyTypeSourceKind.Tiny => 0,
                TinyTypeSourceKind.CSharp => 1,
                TinyTypeSourceKind.Framework => 2,
                _ => 3
            })
            .ThenBy(candidate => candidate.Namespace, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.QualifiedName, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Origin, StringComparer.Ordinal)
            .ToArray();

        var selected = orderedCandidates[0];

        if (orderedCandidates.Length > 1)
        {
            var candidateText = string.Join(
                "; ",
                orderedCandidates.Select(candidate =>
                    $"{candidate.QualifiedName} [{candidate.SourceKind}: {candidate.Origin}]"));

            diagnostics.Add(new TinyDiagnostic(
                TinyDiagnosticSeverity.Warning,
                $"Type name '{type.Name}' was found {orderedCandidates.Length} times. " +
                $"Selected '{selected.QualifiedName}'. Candidates: {candidateText}.",
                document.SourceFilePath,
                property.TypeLine,
                property.TypeColumn,
                Code: TinyDiagnosticCodes.AmbiguousType));

            // Fully qualify an ambiguous selection so generated C# is deterministic
            // even if multiple conflicting imports are present.
            return type with
            {
                Name = selected.QualifiedName,
                ResolvedNamespace = selected.Namespace
            };
        }

        if (!string.IsNullOrWhiteSpace(selected.Namespace) &&
            !string.Equals(
                selected.Namespace,
                document.Namespace,
                StringComparison.Ordinal) &&
            !document.Usings.Contains(
                selected.Namespace,
                StringComparer.Ordinal))
        {
            document.Usings.Add(selected.Namespace);
        }

        return type with
        {
            ResolvedNamespace = selected.Namespace
        };
    }

    private static IReadOnlyDictionary<TypeKey, IReadOnlyList<TypeCandidate>> BuildTypeIndex(
        Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation,
        ISet<string> tinyStubPaths)
    {
        var index = new Dictionary<TypeKey, List<TypeCandidate>>();

        VisitNamespace(
            compilation.Assembly.GlobalNamespace,
            currentAssembly: true,
            tinyStubPaths,
            index);

        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols
                     .GroupBy(symbol => symbol.Identity.ToString(), StringComparer.Ordinal)
                     .Select(group => group.First()))
        {
            VisitNamespace(
                assembly.GlobalNamespace,
                currentAssembly: false,
                tinyStubPaths,
                index);
        }

        return index.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<TypeCandidate>)pair.Value
                .GroupBy(
                    candidate => candidate.QualifiedName + "|" + candidate.Origin,
                    StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray());
    }

    private static void VisitNamespace(
        INamespaceSymbol namespaceSymbol,
        bool currentAssembly,
        ISet<string> tinyStubPaths,
        IDictionary<TypeKey, List<TypeCandidate>> index)
    {
        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            VisitType(
                type,
                currentAssembly,
                tinyStubPaths,
                index);
        }

        foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            VisitNamespace(
                childNamespace,
                currentAssembly,
                tinyStubPaths,
                index);
        }
    }

    private static void VisitType(
        INamedTypeSymbol type,
        bool currentAssembly,
        ISet<string> tinyStubPaths,
        IDictionary<TypeKey, List<TypeCandidate>> index)
    {
        if (type.CanBeReferencedByName &&
            IsAccessible(type, currentAssembly))
        {
            var sourceKind = currentAssembly
                ? GetCurrentProjectSourceKind(type, tinyStubPaths)
                : TinyTypeSourceKind.Framework;
            var namespaceName = type.ContainingNamespace is null ||
                                type.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : type.ContainingNamespace.ToDisplayString();
            var candidate = new TypeCandidate(
                type.Name,
                GetQualifiedName(type),
                namespaceName,
                type.Arity,
                type.TypeKind,
                type.DeclaredAccessibility,
                sourceKind,
                type.ContainingAssembly?.Identity.Name ?? string.Empty);

            var key = new TypeKey(candidate.Name, candidate.Arity);

            if (!index.TryGetValue(key, out var bucket))
            {
                bucket = new List<TypeCandidate>();
                index[key] = bucket;
            }

            bucket.Add(candidate);
        }

        foreach (var nestedType in type.GetTypeMembers())
        {
            VisitType(
                nestedType,
                currentAssembly,
                tinyStubPaths,
                index);
        }
    }

    private static bool IsAccessible(
        INamedTypeSymbol type,
        bool currentAssembly)
    {
        var allowed = currentAssembly
            ? type.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal
            : type.DeclaredAccessibility == Accessibility.Public;

        if (!allowed)
        {
            return false;
        }

        var containingType = type.ContainingType;

        while (containingType is not null)
        {
            if (currentAssembly)
            {
                if (containingType.DeclaredAccessibility is not
                    (Accessibility.Public or Accessibility.Internal))
                {
                    return false;
                }
            }
            else if (containingType.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }

            containingType = containingType.ContainingType;
        }

        return true;
    }

    private static TinyTypeSourceKind GetCurrentProjectSourceKind(
        INamedTypeSymbol type,
        ISet<string> tinyStubPaths)
    {
        foreach (var location in type.Locations)
        {
            var path = location.SourceTree?.FilePath;

            if (path is not null && tinyStubPaths.Contains(path))
            {
                return TinyTypeSourceKind.Tiny;
            }
        }

        return TinyTypeSourceKind.CSharp;
    }

    private static string GetQualifiedName(INamedTypeSymbol type)
    {
        var typeNames = new Stack<string>();
        INamedTypeSymbol? current = type;

        while (current is not null)
        {
            typeNames.Push(current.Name);
            current = current.ContainingType;
        }

        var namespaceName = type.ContainingNamespace is null ||
                            type.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : type.ContainingNamespace.ToDisplayString();
        var typeName = string.Join(".", typeNames);

        return string.IsNullOrWhiteSpace(namespaceName)
            ? typeName
            : $"{namespaceName}.{typeName}";
    }

    private static IEnumerable<string> DiscoverHandwrittenCSharp(
        string projectDirectory)
    {
        return Directory
            .EnumerateFiles(
                projectDirectory,
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !IsBuildOutputPath(projectDirectory, path))
            .Where(path => !File.Exists(Path.ChangeExtension(path, ".tcs")))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(Path.GetFullPath);
    }

    private static bool IsBuildOutputPath(
        string projectDirectory,
        string path)
    {
        var relative = Path.GetRelativePath(projectDirectory, path);
        var segments = relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        return segments.Any(segment =>
            string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
    }

    private static string CreateDeclarationStub(TinyDocument document)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(document.Namespace))
        {
            sb.Append("namespace ")
                .Append(document.Namespace)
                .AppendLine(";");
            sb.AppendLine();
        }

        sb.Append(document.TypeDeclaration.Accessibility == TinyAccessibility.Public
                ? "public"
                : "internal")
            .Append(" class ")
            .Append(document.ClassName)
            .AppendLine(" { }");

        return sb.ToString();
    }

    private sealed record TypeKey(
        string Name,
        int Arity);

    private sealed record TypeCandidate(
        string Name,
        string QualifiedName,
        string Namespace,
        int Arity,
        TypeKind Kind,
        Accessibility Accessibility,
        TinyTypeSourceKind SourceKind,
        string Origin);

    private enum TinyTypeSourceKind
    {
        Tiny,
        CSharp,
        Framework
    }
}
