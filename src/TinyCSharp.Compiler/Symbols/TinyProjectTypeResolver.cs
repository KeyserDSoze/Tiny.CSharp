using System.Text;
using Microsoft.CodeAnalysis;
using TinyCSharp.Compiler.Compilation;
using TinyCSharp.Compiler.Decompilation;
using TinyCSharp.Compiler.Diagnostics;
using TinyCSharp.Compiler.Language;
using TinyCSharp.Compiler.Parsing;
using TinyCSharp.Compiler.Projects;

namespace TinyCSharp.Compiler.Symbols;

public sealed class TinyProjectTypeResolver
{
    private const string TinyStubPrefix = "__tiny_stub__/";

    public async Task<IReadOnlyList<TinyDiagnostic>> ResolveAsync(
        string projectPath,
        IReadOnlyList<TinySyntaxTree> documents,
        bool emitAutomaticUsings = true,
        CancellationToken cancellationToken = default)
    {
        if (documents.Count == 0)
        {
            return Array.Empty<TinyDiagnostic>();
        }

        var needsTypeResolution = documents.Any(document =>
            document.Properties.Any(property =>
                NeedsResolution(property.Type)));
        var needsUsingValidation = documents.Any(document =>
            document.Usings.Count > 0);

        if (!needsTypeResolution && !needsUsingValidation)
        {
            return Array.Empty<TinyDiagnostic>();
        }

        projectPath = Path.GetFullPath(projectPath);
        var projectDirectory = Path.GetDirectoryName(projectPath) ?? string.Empty;
        var diagnostics = new List<TinyDiagnostic>();
        var graph = new TinyProjectReferenceGraph().Load(projectPath);
        var rootNode = graph.First(node =>
            string.Equals(
                node.ProjectPath,
                projectPath,
                StringComparison.OrdinalIgnoreCase));

        var metadataLoader = new TinyProjectMetadataReferenceLoader();
        var metadataByPath = new Dictionary<string, TinyMetadataReferenceInfo>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var node in graph)
        {
            var metadataResult = metadataLoader.Load(
                node.ProjectPath,
                warnWhenAssetsMissing: node.Depth == 0);

            if (node.Depth == 0)
            {
                diagnostics.AddRange(metadataResult.Diagnostics);
            }

            foreach (var reference in metadataResult.References)
            {
                var effectiveReference = reference with
                {
                    IsDirect = node.Depth == 0 && reference.IsDirect
                };

                if (!metadataByPath.TryGetValue(
                        effectiveReference.Path,
                        out var existing) ||
                    (!existing.IsDirect && effectiveReference.IsDirect))
                {
                    metadataByPath[effectiveReference.Path] = effectiveReference;
                }
            }
        }

        var metadataReferences = metadataByPath.Values
            .Select(reference =>
                MetadataReference.CreateFromFile(reference.Path))
            .ToArray();
        var externalKinds = BuildExternalAssemblyKindMap(
            metadataByPath.Values);

        var sourceDocuments = new List<CSharpSourceDocument>();
        var tinyStubPaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < documents.Count; i++)
        {
            var document = documents[i];
            var stubPath = $"{TinyStubPrefix}current/{i}-{document.ClassName}.cs";

            tinyStubPaths.Add(stubPath);
            sourceDocuments.Add(new CSharpSourceDocument(
                stubPath,
                CreateDeclarationStub(document)));
        }

        foreach (var csharpPath in DiscoverHandwrittenCSharp(projectDirectory))
        {
            var source = await File.ReadAllTextAsync(
                csharpPath,
                cancellationToken);
            sourceDocuments.Add(new CSharpSourceDocument(
                csharpPath,
                source));
        }

        var semanticCompilation = CSharpSemanticCompilation.Create(
            sourceDocuments,
            metadataReferences,
            rootNode.AssemblyName);
        var index = new Dictionary<TypeKey, List<TypeCandidate>>();
        var knownNamespaces = new HashSet<string>(StringComparer.Ordinal);

        CollectNamespaces(
            semanticCompilation.Compilation.Assembly.GlobalNamespace,
            knownNamespaces);

        VisitNamespace(
            semanticCompilation.Compilation.Assembly.GlobalNamespace,
            allowInternal: true,
            type => GetCurrentProjectSourceKind(
                type,
                tinyStubPaths),
            index);

        foreach (var assembly in semanticCompilation
                     .Compilation
                     .SourceModule
                     .ReferencedAssemblySymbols
                     .GroupBy(
                         symbol => symbol.Identity.ToString(),
                         StringComparer.Ordinal)
                     .Select(group => group.First()))
        {
            var sourceKind = externalKinds.TryGetValue(
                assembly.Identity.Name,
                out var externalKind)
                ? externalKind
                : TinyTypeSourceKind.Framework;

            CollectNamespaces(
                assembly.GlobalNamespace,
                knownNamespaces);

            VisitNamespace(
                assembly.GlobalNamespace,
                allowInternal: false,
                _ => sourceKind,
                index);
        }

        foreach (var node in graph.Where(node => node.Depth > 0))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var referencedCompilation = await CreateReferencedProjectCompilationAsync(
                node,
                metadataReferences,
                cancellationToken);

            CollectNamespaces(
                referencedCompilation.Compilation.Assembly.GlobalNamespace,
                knownNamespaces);

            VisitNamespace(
                referencedCompilation.Compilation.Assembly.GlobalNamespace,
                allowInternal: false,
                _ => node.Depth == 1
                    ? TinyTypeSourceKind.DirectProjectReference
                    : TinyTypeSourceKind.TransitiveProjectReference,
                index);
        }

        var frozenIndex = FreezeIndex(index);

        foreach (var document in documents)
        {
            foreach (var usingNamespace in document.Usings)
            {
                if (!knownNamespaces.Contains(usingNamespace))
                {
                    diagnostics.Add(new TinyDiagnostic(
                        TinyDiagnosticSeverity.Error,
                        $"Using namespace '{usingNamespace}' could not be resolved in the project symbol universe.",
                        document.SourceFilePath,
                        1,
                        1,
                        Code: TinyDiagnosticCodes.UnknownUsingNamespace));
                }
            }

            if (!needsTypeResolution)
            {
                continue;
            }

            foreach (var property in document.Properties)
            {
                property.Type = ResolveType(
                    property.Type,
                    property,
                    document,
                    frozenIndex,
                    diagnostics,
                    emitAutomaticUsings);
            }
        }

        return diagnostics;
    }

    private static async Task<CSharpSemanticCompilation>
        CreateReferencedProjectCompilationAsync(
            TinyProjectReferenceNode node,
            IReadOnlyList<MetadataReference> metadataReferences,
            CancellationToken cancellationToken)
    {
        var projectDirectory =
            Path.GetDirectoryName(node.ProjectPath) ?? string.Empty;
        var sources = new List<CSharpSourceDocument>();
        var index = 0;

        foreach (var tcsPath in Directory
                     .EnumerateFiles(
                         projectDirectory,
                         "*.tcs",
                         SearchOption.AllDirectories)
                     .Where(path =>
                         !IsBuildOutputPath(projectDirectory, path))
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var source = await File.ReadAllTextAsync(
                tcsPath,
                cancellationToken);
            var tree = new TinyParser().Parse(source, tcsPath);

            if (!tree.IsValid)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(tree.Namespace))
            {
                tree.Namespace = InferNamespace(
                    node.RootNamespace,
                    projectDirectory,
                    tcsPath);
            }

            sources.Add(new CSharpSourceDocument(
                $"{TinyStubPrefix}reference/{node.Depth}/{index++}-{tree.ClassName}.cs",
                CreateDeclarationStub(tree)));
        }

        foreach (var csharpPath in DiscoverHandwrittenCSharp(projectDirectory))
        {
            var source = await File.ReadAllTextAsync(
                csharpPath,
                cancellationToken);
            sources.Add(new CSharpSourceDocument(
                csharpPath,
                source));
        }

        return CSharpSemanticCompilation.Create(
            sources,
            metadataReferences,
            node.AssemblyName);
    }

    private static Dictionary<string, TinyTypeSourceKind>
        BuildExternalAssemblyKindMap(
            IEnumerable<TinyMetadataReferenceInfo> references)
    {
        return references
            .GroupBy(
                reference => reference.AssemblyName,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Any(reference => reference.IsDirect)
                    ? TinyTypeSourceKind.DirectExternalAssembly
                    : TinyTypeSourceKind.TransitiveExternalAssembly,
                StringComparer.OrdinalIgnoreCase);
    }

    private static bool NeedsResolution(TinyType type)
    {
        if (type.TypeArguments.Any(NeedsResolution))
        {
            return true;
        }

        return TinyLanguage.GetCanonicalTypeToken(type.Name) == type.Name &&
               !type.Name.Contains('.');
    }

    private static TinyType ResolveType(
        TinyType type,
        TinyProperty property,
        TinySyntaxTree document,
        IReadOnlyDictionary<TypeKey, IReadOnlyList<TypeCandidate>> index,
        List<TinyDiagnostic> diagnostics,
        bool emitAutomaticUsings)
    {
        var resolvedArguments = type.TypeArguments
            .Select(argument => ResolveType(
                argument,
                property,
                document,
                index,
                diagnostics,
                emitAutomaticUsings))
            .ToArray();

        type = type with { TypeArguments = resolvedArguments };

        if (TinyLanguage.GetCanonicalTypeToken(type.Name) != type.Name ||
            type.Name.Contains('.'))
        {
            return type;
        }

        var key = new TypeKey(
            type.Name,
            type.TypeArguments.Count);

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

        var sameNamespaceCandidates = candidates
            .Where(candidate =>
                string.Equals(
                    candidate.Namespace,
                    document.Namespace,
                    StringComparison.Ordinal))
            .ToArray();

        var importedCandidates = candidates
            .Where(candidate =>
                document.Usings.Contains(
                    candidate.Namespace,
                    StringComparer.Ordinal))
            .ToArray();

        var bindingCandidates = sameNamespaceCandidates.Length > 0
            ? sameNamespaceCandidates
            : importedCandidates.Length > 0
                ? importedCandidates
                : candidates.ToArray();

        var orderedCandidates = bindingCandidates
            .OrderBy(candidate => GetSourcePriority(candidate.SourceKind))
            .ThenBy(
                candidate => candidate.Namespace,
                StringComparer.Ordinal)
            .ThenBy(
                candidate => candidate.QualifiedName,
                StringComparer.Ordinal)
            .ThenBy(
                candidate => candidate.Origin,
                StringComparer.Ordinal)
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
                StringComparison.Ordinal))
        {
            if (!emitAutomaticUsings)
            {
                return type with
                {
                    Name = selected.QualifiedName,
                    ResolvedNamespace = selected.Namespace
                };
            }

            if (!document.Usings.Contains(
                    selected.Namespace,
                    StringComparer.Ordinal))
            {
                document.Usings.Add(selected.Namespace);
            }
        }

        return type with
        {
            ResolvedNamespace = selected.Namespace
        };
    }

    private static int GetSourcePriority(
        TinyTypeSourceKind sourceKind)
    {
        return sourceKind switch
        {
            TinyTypeSourceKind.Tiny => 0,
            TinyTypeSourceKind.CSharp => 1,
            TinyTypeSourceKind.DirectProjectReference => 2,
            TinyTypeSourceKind.TransitiveProjectReference => 3,
            TinyTypeSourceKind.DirectExternalAssembly => 4,
            TinyTypeSourceKind.TransitiveExternalAssembly => 5,
            TinyTypeSourceKind.Framework => 6,
            _ => 7
        };
    }

    private static IReadOnlyDictionary<TypeKey, IReadOnlyList<TypeCandidate>>
        FreezeIndex(
            IReadOnlyDictionary<TypeKey, List<TypeCandidate>> index)
    {
        return index.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<TypeCandidate>)pair.Value
                .GroupBy(
                    candidate =>
                        candidate.QualifiedName + "|" +
                        candidate.Origin + "|" +
                        candidate.SourceKind,
                    StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray());
    }

    private static void CollectNamespaces(
        INamespaceSymbol namespaceSymbol,
        ISet<string> namespaces)
    {
        if (!namespaceSymbol.IsGlobalNamespace)
        {
            namespaces.Add(namespaceSymbol.ToDisplayString());
        }

        foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            CollectNamespaces(
                childNamespace,
                namespaces);
        }
    }

    private static void VisitNamespace(
        INamespaceSymbol namespaceSymbol,
        bool allowInternal,
        Func<INamedTypeSymbol, TinyTypeSourceKind> sourceKindSelector,
        IDictionary<TypeKey, List<TypeCandidate>> index)
    {
        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            VisitType(
                type,
                allowInternal,
                sourceKindSelector,
                index);
        }

        foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            VisitNamespace(
                childNamespace,
                allowInternal,
                sourceKindSelector,
                index);
        }
    }

    private static void VisitType(
        INamedTypeSymbol type,
        bool allowInternal,
        Func<INamedTypeSymbol, TinyTypeSourceKind> sourceKindSelector,
        IDictionary<TypeKey, List<TypeCandidate>> index)
    {
        if (type.CanBeReferencedByName &&
            IsAccessible(type, allowInternal))
        {
            var namespaceName =
                type.ContainingNamespace is null ||
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
                sourceKindSelector(type),
                type.ContainingAssembly?.Identity.Name ?? string.Empty);
            var key = new TypeKey(
                candidate.Name,
                candidate.Arity);

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
                allowInternal,
                sourceKindSelector,
                index);
        }
    }

    private static bool IsAccessible(
        INamedTypeSymbol type,
        bool allowInternal)
    {
        var allowed = allowInternal
            ? type.DeclaredAccessibility is
                Accessibility.Public or Accessibility.Internal
            : type.DeclaredAccessibility == Accessibility.Public;

        if (!allowed)
        {
            return false;
        }

        var containingType = type.ContainingType;

        while (containingType is not null)
        {
            if (allowInternal)
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

            if (path is not null &&
                tinyStubPaths.Contains(path))
            {
                return TinyTypeSourceKind.Tiny;
            }
        }

        return TinyTypeSourceKind.CSharp;
    }

    private static string GetQualifiedName(
        INamedTypeSymbol type)
    {
        var typeNames = new Stack<string>();
        INamedTypeSymbol? current = type;

        while (current is not null)
        {
            typeNames.Push(current.Name);
            current = current.ContainingType;
        }

        var namespaceName =
            type.ContainingNamespace is null ||
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
            .Where(path =>
                !IsBuildOutputPath(projectDirectory, path))
            .Where(path =>
                !File.Exists(Path.ChangeExtension(path, ".tcs")))
            .OrderBy(
                path => path,
                StringComparer.Ordinal)
            .Select(Path.GetFullPath);
    }

    private static bool IsBuildOutputPath(
        string projectDirectory,
        string path)
    {
        var relative = Path.GetRelativePath(
            projectDirectory,
            path);
        var segments = relative.Split(
            new[]
            {
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            },
            StringSplitOptions.RemoveEmptyEntries);

        return segments.Any(segment =>
            string.Equals(
                segment,
                "bin",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                segment,
                "obj",
                StringComparison.OrdinalIgnoreCase));
    }

    private static string InferNamespace(
        string rootNamespace,
        string projectDirectory,
        string tcsFilePath)
    {
        var sourceDirectory =
            Path.GetDirectoryName(tcsFilePath) ?? projectDirectory;
        var relativeDirectory = Path.GetRelativePath(
            projectDirectory,
            sourceDirectory);

        if (relativeDirectory == ".")
        {
            return rootNamespace;
        }

        var segments = relativeDirectory
            .Split(
                new[]
                {
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                },
                StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeNamespaceSegment)
            .Where(segment =>
                !string.IsNullOrWhiteSpace(segment));

        var suffix = string.Join('.', segments);

        return string.IsNullOrWhiteSpace(suffix)
            ? rootNamespace
            : $"{rootNamespace}.{suffix}";
    }

    private static string NormalizeNamespaceSegment(
        string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return string.Empty;
        }

        var characters = segment
            .Select(character =>
                char.IsLetterOrDigit(character) ||
                character == '_'
                    ? character
                    : '_')
            .ToArray();
        var normalized = new string(characters);

        if (char.IsDigit(normalized[0]))
        {
            normalized = "_" + normalized;
        }

        return normalized;
    }

    private static string CreateDeclarationStub(
        TinyDocument document)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(document.Namespace))
        {
            sb.Append("namespace ")
                .Append(document.Namespace)
                .AppendLine(";");
            sb.AppendLine();
        }

        sb.Append(
                document.TypeDeclaration.Accessibility ==
                TinyAccessibility.Public
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
        DirectProjectReference,
        TransitiveProjectReference,
        DirectExternalAssembly,
        TransitiveExternalAssembly,
        Framework
    }
}
