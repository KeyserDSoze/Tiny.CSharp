using System.Xml.Linq;
using TinyCSharp.Compiler.Diagnostics;
using TinyCSharp.Compiler.Generation;
using TinyCSharp.Compiler.Parsing;
using TinyCSharp.Compiler.Symbols;

namespace TinyCSharp.Compiler.Compilation;

public sealed class TinyProjectCompiler
{
    public async Task<TinyProjectCompilationResult> CompileAsync(
        string projectPath,
        TinyCompilerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new TinyCompilerOptions();

        projectPath = Path.GetFullPath(projectPath);

        var results = new List<TinyFileCompilationResult>();
        var diagnostics = new List<TinyDiagnostic>();
        var projectMetadata = ReadProjectMetadata(projectPath, diagnostics);
        var projectDirectory = Path.GetDirectoryName(projectPath);

        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            diagnostics.Add(new TinyDiagnostic(
                TinyDiagnosticSeverity.Error,
                "Could not determine project directory.",
                projectPath,
                1,
                1,
                Code: TinyDiagnosticCodes.ProjectDirectoryUnavailable));

            return new TinyProjectCompilationResult(
                false,
                results,
                diagnostics);
        }

        var searchOption = options.Recursive
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;
        var tcsFiles = Directory
            .GetFiles(projectDirectory, "*.tcs", searchOption)
            .Where(path => !IsBuildOutputPath(projectDirectory, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var parsedFiles = new List<TinyParsedFile>();

        foreach (var tcsFile in tcsFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var content = await File.ReadAllTextAsync(
                    tcsFile,
                    cancellationToken);
                var parser = new TinyParser();
                var syntaxTree = parser.Parse(content, tcsFile);

                if (string.IsNullOrWhiteSpace(syntaxTree.Namespace))
                {
                    syntaxTree.Namespace = InferNamespace(
                        projectPath,
                        projectMetadata,
                        tcsFile);
                }

                if (!syntaxTree.IsValid)
                {
                    var fileDiagnostics = syntaxTree.Diagnostics.ToArray();

                    results.Add(new TinyFileCompilationResult(
                        tcsFile,
                        false,
                        fileDiagnostics));
                    diagnostics.AddRange(fileDiagnostics);
                    continue;
                }

                parsedFiles.Add(new TinyParsedFile(
                    tcsFile,
                    syntaxTree));
            }
            catch (Exception ex)
            {
                var diagnostic = new TinyDiagnostic(
                    TinyDiagnosticSeverity.Error,
                    $"Failed to parse {tcsFile}: {ex.Message}",
                    tcsFile,
                    1,
                    1,
                    Code: TinyDiagnosticCodes.FileCompilationFailed);

                results.Add(new TinyFileCompilationResult(
                    tcsFile,
                    false,
                    new[] { diagnostic }));
                diagnostics.Add(diagnostic);
            }
        }

        IReadOnlyList<TinyDiagnostic> resolutionDiagnostics;

        try
        {
            var resolver = new TinyProjectTypeResolver();
            resolutionDiagnostics = await resolver.ResolveAsync(
                projectPath,
                parsedFiles.Select(file => file.SyntaxTree).ToArray(),
                options.EmitAutomaticUsings,
                cancellationToken);
        }
        catch (Exception ex)
        {
            var diagnostic = new TinyDiagnostic(
                TinyDiagnosticSeverity.Error,
                $"Tiny.CSharp type resolution failed: {ex.Message}",
                projectPath,
                1,
                1,
                Code: TinyDiagnosticCodes.InternalCompilerFailure);

            diagnostics.Add(diagnostic);

            foreach (var parsedFile in parsedFiles)
            {
                results.Add(new TinyFileCompilationResult(
                    parsedFile.FilePath,
                    false,
                    Array.Empty<TinyDiagnostic>()));
            }

            return CreateResult(
                results,
                diagnostics,
                options);
        }

        var diagnosticsByFile = resolutionDiagnostics
            .GroupBy(
                diagnostic => diagnostic.FilePath,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var parsedFile in parsedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileDiagnostics = diagnosticsByFile.TryGetValue(
                parsedFile.FilePath,
                out var resolvedDiagnostics)
                ? resolvedDiagnostics.ToList()
                : new List<TinyDiagnostic>();

            if (options.TreatWarningsAsErrors &&
                fileDiagnostics.Any(diagnostic =>
                    diagnostic.Severity == TinyDiagnosticSeverity.Warning))
            {
                results.Add(new TinyFileCompilationResult(
                    parsedFile.FilePath,
                    false,
                    fileDiagnostics));
                diagnostics.AddRange(fileDiagnostics);
                continue;
            }

            var result = await GenerateFileAsync(
                parsedFile,
                fileDiagnostics,
                cancellationToken);

            results.Add(result);

            if (result.Diagnostics is not null)
            {
                diagnostics.AddRange(result.Diagnostics);
            }
        }

        return CreateResult(
            results,
            diagnostics,
            options);
    }

    private static async Task<TinyFileCompilationResult> GenerateFileAsync(
        TinyParsedFile parsedFile,
        List<TinyDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var generator = new CSharpGenerator();
        var csharp = generator.Generate(parsedFile.SyntaxTree);
        var csharpPath = Path.ChangeExtension(
            parsedFile.FilePath,
            ".cs");
        var tempPath = csharpPath + ".tmp";

        try
        {
            await File.WriteAllTextAsync(
                tempPath,
                csharp,
                cancellationToken);
            File.Move(
                tempPath,
                csharpPath,
                overwrite: true);

            return new TinyFileCompilationResult(
                parsedFile.FilePath,
                true,
                diagnostics);
        }
        catch (Exception ex)
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            diagnostics.Add(new TinyDiagnostic(
                TinyDiagnosticSeverity.Error,
                $"The generated file '{Path.GetFileName(csharpPath)}' could not be replaced: {ex.Message}",
                parsedFile.FilePath,
                1,
                1,
                Code: TinyDiagnosticCodes.OutputReplacementFailed));

            return new TinyFileCompilationResult(
                parsedFile.FilePath,
                false,
                diagnostics);
        }
    }

    private static TinyProjectCompilationResult CreateResult(
        List<TinyFileCompilationResult> results,
        List<TinyDiagnostic> diagnostics,
        TinyCompilerOptions options)
    {
        results.Sort((left, right) =>
            StringComparer.Ordinal.Compare(
                left.FilePath,
                right.FilePath));

        var hasBlockingDiagnostic = diagnostics.Any(diagnostic =>
            diagnostic.Severity == TinyDiagnosticSeverity.Error ||
            (options.TreatWarningsAsErrors &&
             diagnostic.Severity == TinyDiagnosticSeverity.Warning));

        return new TinyProjectCompilationResult(
            !hasBlockingDiagnostic &&
            results.All(result => result.Success),
            results,
            diagnostics);
    }

    private static TinyProjectMetadata ReadProjectMetadata(
        string projectPath,
        List<TinyDiagnostic> diagnostics)
    {
        var metadata = new TinyProjectMetadata(
            Path.GetFileNameWithoutExtension(projectPath),
            null);

        try
        {
            var document = XDocument.Load(projectPath);
            var rootNamespace = document
                .Descendants()
                .FirstOrDefault(element =>
                    element.Name.LocalName == "RootNamespace")
                ?.Value
                ?.Trim();
            var assemblyName = document
                .Descendants()
                .FirstOrDefault(element =>
                    element.Name.LocalName == "AssemblyName")
                ?.Value
                ?.Trim();

            metadata = new TinyProjectMetadata(
                string.IsNullOrWhiteSpace(assemblyName)
                    ? Path.GetFileNameWithoutExtension(projectPath)
                    : assemblyName,
                rootNamespace);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new TinyDiagnostic(
                TinyDiagnosticSeverity.Warning,
                $"Could not read project metadata from '{projectPath}': {ex.Message}",
                projectPath,
                1,
                1,
                Code: TinyDiagnosticCodes.ProjectMetadataUnavailable));
        }

        return metadata;
    }

    private static string InferNamespace(
        string projectPath,
        TinyProjectMetadata metadata,
        string tcsFilePath)
    {
        var projectDirectory =
            Path.GetDirectoryName(projectPath) ?? string.Empty;
        var sourceDirectory =
            Path.GetDirectoryName(tcsFilePath) ?? projectDirectory;
        var baseNamespace = string.IsNullOrWhiteSpace(metadata.RootNamespace)
            ? metadata.AssemblyName
            : metadata.RootNamespace;

        var relativeDirectory = Path.GetRelativePath(
            projectDirectory,
            sourceDirectory);

        if (relativeDirectory == ".")
        {
            return baseNamespace;
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
            .Where(segment => !string.IsNullOrWhiteSpace(segment));

        var suffix = string.Join('.', segments);

        return string.IsNullOrWhiteSpace(suffix)
            ? baseNamespace
            : $"{baseNamespace}.{suffix}";
    }

    private static string NormalizeNamespaceSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return string.Empty;
        }

        var chars = segment
            .Select(character =>
                char.IsLetterOrDigit(character) || character == '_'
                    ? character
                    : '_')
            .ToArray();
        var normalized = new string(chars);

        if (char.IsDigit(normalized[0]))
        {
            normalized = "_" + normalized;
        }

        return normalized;
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

    private sealed record TinyParsedFile(
        string FilePath,
        TinySyntaxTree SyntaxTree);
}

internal sealed record TinyProjectMetadata(
    string AssemblyName,
    string? RootNamespace);

public sealed record TinyCompilerOptions(
    bool Recursive = true,
    bool OverwriteGeneratedFiles = true,
    bool EmitAutomaticUsings = true,
    bool TreatWarningsAsErrors = false);

public sealed record TinyProjectCompilationResult(
    bool Success,
    IReadOnlyList<TinyFileCompilationResult> Files,
    IReadOnlyList<TinyDiagnostic> Diagnostics);

public sealed record TinyFileCompilationResult(
    string FilePath,
    bool Success,
    IReadOnlyList<TinyDiagnostic>? Diagnostics);

public sealed record TinyDiagnostic(
    TinyDiagnosticSeverity Severity,
    string Message,
    string FilePath,
    int Line,
    int Column,
    IReadOnlyList<TinyDiagnostic>? RelatedInformation = null,
    string Code = "TCS0000");

public enum TinyDiagnosticSeverity
{
    Error,
    Warning,
    Info
}
