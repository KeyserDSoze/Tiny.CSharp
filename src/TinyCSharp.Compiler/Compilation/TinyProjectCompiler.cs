using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using TinyCSharp.Compiler.Generation;
using TinyCSharp.Compiler.Parsing;

namespace TinyCSharp.Compiler.Compilation;

public sealed class TinyProjectCompiler
{
    public async Task<TinyProjectCompilationResult> CompileAsync(
        string projectPath,
        TinyCompilerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new TinyCompilerOptions();
        
        var results = new List<TinyFileCompilationResult>();
        var diagnostics = new List<TinyDiagnostic>();
        var projectMetadata = ReadProjectMetadata(projectPath, diagnostics);
        
        // Get the project directory
        var projectDir = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrEmpty(projectDir))
        {
            diagnostics.Add(new TinyDiagnostic(
                TinyDiagnosticSeverity.Error, 
                "Could not determine project directory", 
                projectPath, 
                0, 
                0));
            return new TinyProjectCompilationResult(false, results, diagnostics);
        }
        
        // Discover all .tcs files in the project directory and subdirectories
        var tcsFiles = Directory.GetFiles(projectDir, "*.tcs", options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
        
        foreach (var tcsFile in tcsFiles)
        {
            try
            {
                var result = await CompileFileAsync(tcsFile, projectPath, projectMetadata, options, cancellationToken);
                results.Add(result);
                
                if (result.Diagnostics != null)
                {
                    diagnostics.AddRange(result.Diagnostics);
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add(new TinyDiagnostic(
                    TinyDiagnosticSeverity.Error, 
                    $"Failed to compile {tcsFile}: {ex.Message}", 
                    tcsFile, 
                    0, 
                    0));
            }
        }
        
        return new TinyProjectCompilationResult(
            diagnostics.Count == 0, 
            results, 
            diagnostics);
    }
    
    private async Task<TinyFileCompilationResult> CompileFileAsync(
        string tcsFilePath, 
        string projectPath,
        TinyProjectMetadata projectMetadata,
        TinyCompilerOptions options, 
        CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(tcsFilePath, cancellationToken);
        
        // Parse the .tcs file
        var parser = new TinyParser();
        var syntaxTree = parser.Parse(content);
        syntaxTree.SourceFilePath = tcsFilePath;
        if (string.IsNullOrWhiteSpace(syntaxTree.Namespace))
        {
            syntaxTree.Namespace = InferNamespace(projectPath, projectMetadata, tcsFilePath);
        }
        
        // Validate the syntax
        var diagnostics = new List<TinyDiagnostic>();
        
        if (!syntaxTree.IsValid)
        {
            diagnostics.AddRange(syntaxTree.Diagnostics);
            return new TinyFileCompilationResult(tcsFilePath, false, diagnostics);
        }
        
        // Generate C# code
        var generator = new CSharpGenerator();
        var csContent = generator.Generate(syntaxTree);
        
        // Determine output .cs file path
        var csFilePath = tcsFilePath.Substring(0, tcsFilePath.Length - 4) + ".cs";
        
        // Write to temporary file first, then atomically replace
        var tempFilePath = csFilePath + ".tmp";
        
        try
        {
            await File.WriteAllTextAsync(tempFilePath, csContent, cancellationToken);

            File.Move(tempFilePath, csFilePath, true);
            
            return new TinyFileCompilationResult(tcsFilePath, true, diagnostics);
        }
        catch (Exception ex)
        {
            // If anything fails, clean up temp file and return error
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
            
            diagnostics.Add(new TinyDiagnostic(
                TinyDiagnosticSeverity.Error,
                $"The generated file '{Path.GetFileName(csFilePath)}' could not be replaced: {ex.Message}",
                tcsFilePath,
                1,
                1));

            return new TinyFileCompilationResult(tcsFilePath, false, diagnostics);
        }
    }

    private static TinyProjectMetadata ReadProjectMetadata(string projectPath, List<TinyDiagnostic> diagnostics)
    {
        var metadata = new TinyProjectMetadata(Path.GetFileNameWithoutExtension(projectPath), null);

        try
        {
            var document = XDocument.Load(projectPath);
            var rootNamespace = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "RootNamespace")?.Value?.Trim();
            var assemblyName = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "AssemblyName")?.Value?.Trim();
            metadata = new TinyProjectMetadata(string.IsNullOrWhiteSpace(assemblyName) ? Path.GetFileNameWithoutExtension(projectPath) : assemblyName, rootNamespace);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new TinyDiagnostic(
                TinyDiagnosticSeverity.Warning,
                $"Could not read project metadata from '{projectPath}': {ex.Message}",
                projectPath,
                1,
                1));
        }

        return metadata;
    }

    private static string InferNamespace(string projectPath, TinyProjectMetadata metadata, string tcsFilePath)
    {
        var projectDir = Path.GetDirectoryName(projectPath) ?? string.Empty;
        var sourceDir = Path.GetDirectoryName(tcsFilePath) ?? projectDir;
        var baseNamespace = string.IsNullOrWhiteSpace(metadata.RootNamespace)
            ? metadata.AssemblyName
            : metadata.RootNamespace;

        var relativeDir = Path.GetRelativePath(projectDir, sourceDir);
        if (relativeDir == ".")
        {
            return baseNamespace;
        }

        var segments = relativeDir
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeNamespaceSegment)
            .Where(segment => !string.IsNullOrWhiteSpace(segment));

        var suffix = string.Join('.', segments);
        return string.IsNullOrWhiteSpace(suffix) ? baseNamespace : $"{baseNamespace}.{suffix}";
    }

    private static string NormalizeNamespaceSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return string.Empty;
        }

        var chars = segment.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray();
        var normalized = new string(chars);
        if (char.IsDigit(normalized[0]))
        {
            normalized = "_" + normalized;
        }

        return normalized;
    }
}

internal sealed record TinyProjectMetadata(string AssemblyName, string? RootNamespace);

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
    IReadOnlyList<TinyDiagnostic>? RelatedInformation = null);

public enum TinyDiagnosticSeverity
{
    Error,
    Warning,
    Info
}
