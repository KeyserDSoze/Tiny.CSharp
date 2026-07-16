using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
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
                var result = await CompileFileAsync(tcsFile, options, cancellationToken);
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
        TinyCompilerOptions options, 
        CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(tcsFilePath, cancellationToken);
        
        // Parse the .tcs file
        var parser = new TinyParser();
        var syntaxTree = parser.Parse(content);
        
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
            
            // Atomically replace the target file
            if (File.Exists(csFilePath))
            {
                File.Delete(csFilePath);
            }
            
            File.Move(tempFilePath, csFilePath);
            
            return new TinyFileCompilationResult(tcsFilePath, true, diagnostics);
        }
        catch
        {
            // If anything fails, clean up temp file and return error
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
            
            return new TinyFileCompilationResult(tcsFilePath, false, diagnostics);
        }
    }
}

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
