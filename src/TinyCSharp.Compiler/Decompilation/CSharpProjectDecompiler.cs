using TinyCSharp.Compiler.Diagnostics;
using TinyCSharp.Compiler.Generation;

namespace TinyCSharp.Compiler.Decompilation;

public sealed class CSharpProjectDecompiler
{
    public async Task<CSharpProjectDecompilationResult> DecompileAsync(
        string projectPath,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(projectPath))
        {
            return Failure(
                projectPath,
                "Project file not found.");
        }

        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath));
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return Failure(
                projectPath,
                "Could not determine the project directory.");
        }

        var sourceFiles = Directory
            .EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsExcludedDirectory(projectDirectory, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var sourceDocuments = new List<CSharpSourceDocument>();
        var outputCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourceFile in sourceFiles)
        {
            var fullPath = Path.GetFullPath(sourceFile);
            var source = await File.ReadAllTextAsync(fullPath, cancellationToken);

            sourceDocuments.Add(new CSharpSourceDocument(fullPath, source));

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

        var semanticCompilation = CSharpSemanticCompilation.Create(sourceDocuments);
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
            var relativeTinyPath = Path.ChangeExtension(relativePath, ".tcs");
            var outputPath = Path.GetFullPath(
                Path.Combine(outputDirectory, relativeTinyPath));

            Directory.CreateDirectory(
                Path.GetDirectoryName(outputPath) ?? outputDirectory);

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

    private static bool IsExcludedDirectory(
        string projectDirectory,
        string filePath)
    {
        var relative = Path.GetRelativePath(projectDirectory, filePath);
        var segments = relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        if (segments.Any(segment =>
                string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static bool IsGeneratedFileName(string filePath)
    {
        var fileName = Path.GetFileName(filePath);

        return fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGeneratedSource(string source)
    {
        var prefixLength = Math.Min(source.Length, 512);
        return source.AsSpan(0, prefixLength)
            .Contains("<auto-generated", StringComparison.OrdinalIgnoreCase);
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
