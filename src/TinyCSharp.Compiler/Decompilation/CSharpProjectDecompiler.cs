using System.Text;
using Microsoft.CodeAnalysis;
using TinyCSharp.Compiler.Diagnostics;
using TinyCSharp.Compiler.Generation;
using TinyCSharp.Compiler.Language;
using TinyCSharp.Compiler.Parsing;
using TinyCSharp.Compiler.Projects;

namespace TinyCSharp.Compiler.Decompilation;

public sealed class CSharpProjectDecompiler
{
    public async Task<CSharpProjectDecompilationResult> DecompileAsync(
        string projectPath,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        projectPath = Path.GetFullPath(projectPath);

        if (!File.Exists(projectPath))
        {
            return Failure(
                projectPath,
                "Project file not found.");
        }

        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return Failure(
                projectPath,
                "Could not determine the project directory.");
        }

        var graph = new TinyProjectReferenceGraph().Load(projectPath);
        var nodeByPath = graph.ToDictionary(
            node => node.ProjectPath,
            StringComparer.OrdinalIgnoreCase);
        var compilationByProject =
            await BuildReferencedProjectCompilationsAsync(
                graph,
                nodeByPath,
                cancellationToken);

        var rootNode = nodeByPath[projectPath];
        var sourceFiles = Directory
            .EnumerateFiles(
                projectDirectory,
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path =>
                !IsExcludedDirectory(projectDirectory, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var sourceDocuments = new List<CSharpSourceDocument>();
        var outputCandidates = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var sourceFile in sourceFiles)
        {
            var fullPath = Path.GetFullPath(sourceFile);
            var source = await File.ReadAllTextAsync(
                fullPath,
                cancellationToken);

            sourceDocuments.Add(new CSharpSourceDocument(
                fullPath,
                source));

            if (!IsGeneratedFileName(fullPath) &&
                !IsGeneratedSource(source) &&
                !File.Exists(Path.ChangeExtension(fullPath, ".tcs")))
            {
                outputCandidates.Add(fullPath);
            }
        }

        if (outputCandidates.Count == 0)
        {
            return new CSharpProjectDecompilationResult(
                true,
                Array.Empty<CSharpProjectDecompiledFile>(),
                Array.Empty<CSharpProjectDecompilationDiagnostic>());
        }

        var rootReferences = LoadMetadataReferences(rootNode.ProjectPath)
            .Concat(rootNode.ProjectReferences
                .Where(compilationByProject.ContainsKey)
                .Select(referencePath =>
                    compilationByProject[referencePath]
                        .Compilation
                        .ToMetadataReference()))
            .ToArray();

        var semanticCompilation = CSharpSemanticCompilation.Create(
            sourceDocuments,
            rootReferences,
            rootNode.AssemblyName);
        var decompiler = new CSharpDecompiler();
        var formatter = new TinyFormatter();
        var files = new List<CSharpProjectDecompiledFile>();
        var diagnostics = new List<CSharpProjectDecompilationDiagnostic>();

        foreach (var sourceDocument in sourceDocuments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!outputCandidates.Contains(sourceDocument.FilePath))
            {
                continue;
            }

            var result = decompiler.Decompile(
                semanticCompilation,
                sourceDocument.FilePath);

            if (!result.Success || result.Document is null)
            {
                diagnostics.AddRange(result.Diagnostics.Select(
                    diagnostic => new CSharpProjectDecompilationDiagnostic(
                        sourceDocument.FilePath,
                        diagnostic.Code,
                        diagnostic.Message,
                        diagnostic.Line,
                        diagnostic.Column)));
                continue;
            }

            var relativePath = Path.GetRelativePath(
                projectDirectory,
                sourceDocument.FilePath);
            var relativeTinyPath = Path.ChangeExtension(
                relativePath,
                ".tcs");
            var outputPath = Path.GetFullPath(
                Path.Combine(
                    outputDirectory,
                    relativeTinyPath));

            Directory.CreateDirectory(
                Path.GetDirectoryName(outputPath) ??
                outputDirectory);

            await File.WriteAllTextAsync(
                outputPath,
                formatter.Format(result.Document),
                cancellationToken);

            files.Add(new CSharpProjectDecompiledFile(
                sourceDocument.FilePath,
                outputPath));
        }

        return new CSharpProjectDecompilationResult(
            diagnostics.Count == 0,
            files,
            diagnostics);
    }

    private static async Task<
        IReadOnlyDictionary<string, CSharpSemanticCompilation>>
        BuildReferencedProjectCompilationsAsync(
            IReadOnlyList<TinyProjectReferenceNode> graph,
            IReadOnlyDictionary<string, TinyProjectReferenceNode> nodeByPath,
            CancellationToken cancellationToken)
    {
        var compilations =
            new Dictionary<string, CSharpSemanticCompilation>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var node in graph
                     .Where(node => node.Depth > 0)
                     .OrderByDescending(node => node.Depth)
                     .ThenBy(node => node.ProjectPath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sources = await LoadSemanticSourcesAsync(
                node,
                cancellationToken);
            var references = new List<MetadataReference>();

            references.AddRange(
                LoadMetadataReferences(node.ProjectPath));

            foreach (var projectReference in node.ProjectReferences)
            {
                if (compilations.TryGetValue(
                        projectReference,
                        out var referencedCompilation))
                {
                    references.Add(
                        referencedCompilation
                            .Compilation
                            .ToMetadataReference());
                }
                else if (nodeByPath.ContainsKey(projectReference))
                {
                    // A graph cycle or unusual evaluation order should not cause
                    // the decompiler to fabricate a reference. The unresolved
                    // symbol will stay conservative in the semantic model.
                }
            }

            compilations[node.ProjectPath] =
                CSharpSemanticCompilation.Create(
                    sources,
                    references,
                    node.AssemblyName);
        }

        return compilations;
    }

    private static IReadOnlyList<MetadataReference>
        LoadMetadataReferences(string projectPath)
    {
        return new TinyProjectMetadataReferenceLoader()
            .Load(
                projectPath,
                warnWhenAssetsMissing: false)
            .References
            .Select(reference =>
                MetadataReference.CreateFromFile(reference.Path))
            .ToArray();
    }

    private static async Task<IReadOnlyList<CSharpSourceDocument>>
        LoadSemanticSourcesAsync(
            TinyProjectReferenceNode node,
            CancellationToken cancellationToken)
    {
        var projectDirectory =
            Path.GetDirectoryName(node.ProjectPath) ?? string.Empty;
        var sources = new List<CSharpSourceDocument>();

        foreach (var sourceFile in Directory
                     .EnumerateFiles(
                         projectDirectory,
                         "*.cs",
                         SearchOption.AllDirectories)
                     .Where(path =>
                         !IsExcludedDirectory(
                             projectDirectory,
                             path))
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var fullPath = Path.GetFullPath(sourceFile);
            var source = await File.ReadAllTextAsync(
                fullPath,
                cancellationToken);
            sources.Add(new CSharpSourceDocument(
                fullPath,
                source));
        }

        var stubIndex = 0;

        foreach (var tinyPath in Directory
                     .EnumerateFiles(
                         projectDirectory,
                         "*.tcs",
                         SearchOption.AllDirectories)
                     .Where(path =>
                         !IsExcludedDirectory(
                             projectDirectory,
                             path))
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            if (File.Exists(Path.ChangeExtension(tinyPath, ".cs")))
            {
                continue;
            }

            var tinySource = await File.ReadAllTextAsync(
                tinyPath,
                cancellationToken);
            var tree = new TinyParser().Parse(
                tinySource,
                tinyPath);

            if (!tree.IsValid)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(tree.Namespace))
            {
                tree.Namespace = InferNamespace(
                    node.RootNamespace,
                    projectDirectory,
                    tinyPath);
            }

            sources.Add(new CSharpSourceDocument(
                $"__tiny_decompile_reference__/{node.Depth}/{stubIndex++}-{tree.ClassName}.cs",
                CreateDeclarationStub(tree)));
        }

        return sources;
    }

    private static string CreateDeclarationStub(
        TinyDocument document)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(document.Namespace))
        {
            builder.Append("namespace ")
                .Append(document.Namespace)
                .AppendLine(";");
            builder.AppendLine();
        }

        builder.Append(
                document.TypeDeclaration.Accessibility ==
                TinyAccessibility.Public
                    ? "public"
                    : "internal");

        if (document.TypeDeclaration.IsAbstract)
        {
            builder.Append(" abstract");
        }
        else if (document.TypeDeclaration.IsSealed)
        {
            builder.Append(" sealed");
        }

        if (document.TypeDeclaration.IsPartial)
        {
            builder.Append(" partial");
        }

        builder.Append(" class ")
            .Append(document.ClassName)
            .AppendLine(" { }");

        return builder.ToString();
    }

    private static string InferNamespace(
        string rootNamespace,
        string projectDirectory,
        string sourcePath)
    {
        var sourceDirectory =
            Path.GetDirectoryName(sourcePath) ??
            projectDirectory;
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
        var characters = segment
            .Select(character =>
                char.IsLetterOrDigit(character) ||
                character == '_'
                    ? character
                    : '_')
            .ToArray();
        var normalized = new string(characters);

        if (normalized.Length > 0 &&
            char.IsDigit(normalized[0]))
        {
            normalized = "_" + normalized;
        }

        return normalized;
    }

    private static bool IsExcludedDirectory(
        string projectDirectory,
        string filePath)
    {
        var relative = Path.GetRelativePath(
            projectDirectory,
            filePath);
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

    private static bool IsGeneratedFileName(
        string filePath)
    {
        var fileName = Path.GetFileName(filePath);

        return fileName.EndsWith(
                   ".g.cs",
                   StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(
                   ".generated.cs",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGeneratedSource(
        string source)
    {
        var prefixLength = Math.Min(
            source.Length,
            512);

        return source
            .AsSpan(0, prefixLength)
            .Contains(
                "<auto-generated",
                StringComparison.OrdinalIgnoreCase);
    }

    private static CSharpProjectDecompilationResult Failure(
        string filePath,
        string message)
    {
        return new CSharpProjectDecompilationResult(
            false,
            Array.Empty<CSharpProjectDecompiledFile>(),
            new[]
            {
                new CSharpProjectDecompilationDiagnostic(
                    filePath,
                    TinyDiagnosticCodes.ProjectDecompilationFailure,
                    message,
                    1,
                    1)
            });
    }
}

public sealed record CSharpProjectDecompiledFile(
    string SourcePath,
    string OutputPath);

public sealed record CSharpProjectDecompilationDiagnostic(
    string FilePath,
    string Code,
    string Message,
    int Line,
    int Column);

public sealed record CSharpProjectDecompilationResult(
    bool Success,
    IReadOnlyList<CSharpProjectDecompiledFile> Files,
    IReadOnlyList<CSharpProjectDecompilationDiagnostic> Diagnostics);
