using TinyCSharp.Compiler.Benchmarking;
using TinyCSharp.Compiler.Compilation;
using TinyCSharp.Compiler.Decompilation;
using TinyCSharp.Compiler.Generation;

namespace TinyCSharp.Compiler;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 2)
        {
            PrintUsage();
            return 1;
        }

        if (string.Equals(args[0], "compile", StringComparison.OrdinalIgnoreCase))
        {
            return args.Length == 2
                ? await CompileProjectAsync(args[1])
                : InvalidUsage();
        }

        if (string.Equals(args[0], "decompile", StringComparison.OrdinalIgnoreCase))
        {
            return args.Length is 2 or 3
                ? await DecompileFileAsync(args[1], args.Length == 3 ? args[2] : null)
                : InvalidUsage();
        }

        if (string.Equals(args[0], "decompile-project", StringComparison.OrdinalIgnoreCase))
        {
            return args.Length == 3
                ? await DecompileProjectAsync(args[1], args[2])
                : InvalidUsage();
        }

        if (string.Equals(args[0], "benchmark", StringComparison.OrdinalIgnoreCase))
        {
            return args.Length >= 2
                ? await BenchmarkFileAsync(
                    args[1],
                    args.Skip(2))
                : InvalidUsage();
        }

        if (string.Equals(args[0], "benchmark-project", StringComparison.OrdinalIgnoreCase))
        {
            return args.Length >= 2
                ? await BenchmarkProjectAsync(
                    args[1],
                    args.Skip(2))
                : InvalidUsage();
        }

        return InvalidUsage();
    }

    private static async Task<int> CompileProjectAsync(string projectPath)
    {
        if (!File.Exists(projectPath))
        {
            Console.Error.WriteLine($"Project not found: {projectPath}");
            return 1;
        }

        Console.WriteLine($"Compiling project: {projectPath}");

        var compiler = new TinyProjectCompiler();
        var result = await compiler.CompileAsync(projectPath);

        Console.WriteLine(
            result.Success
                ? "Compilation succeeded!"
                : "Compilation failed!");

        foreach (var file in result.Files)
        {
            if (file.Success)
            {
                Console.WriteLine(
                    $"Generated: {Path.ChangeExtension(file.FilePath, ".cs")}");
            }
        }

        foreach (var diagnostic in result.Diagnostics)
        {
            var writer = diagnostic.Severity == TinyDiagnosticSeverity.Error
                ? Console.Error
                : Console.Out;

            writer.WriteLine(
                $"{diagnostic.FilePath}({diagnostic.Line},{diagnostic.Column}): " +
                $"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
        }

        return result.Success ? 0 : 1;
    }

    private static async Task<int> DecompileFileAsync(string csharpPath, string? outputPath)
    {
        if (!File.Exists(csharpPath))
        {
            Console.Error.WriteLine($"C# file not found: {csharpPath}");
            return 1;
        }

        var source = await File.ReadAllTextAsync(csharpPath);
        var decompiler = new CSharpDecompiler();
        var result = decompiler.Decompile(source, csharpPath);

        if (!result.Success || result.Document is null)
        {
            foreach (var diagnostic in result.Diagnostics)
            {
                Console.Error.WriteLine(
                    $"{csharpPath}({diagnostic.Line},{diagnostic.Column}): error {diagnostic.Code}: {diagnostic.Message}");
            }

            return 1;
        }

        var formatter = new TinyFormatter();
        var tinySource = formatter.Format(result.Document);

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            Console.Write(tinySource);
        }
        else
        {
            await File.WriteAllTextAsync(outputPath, tinySource);
            Console.WriteLine($"Generated: {outputPath}");
        }

        return 0;
    }

    private static async Task<int> DecompileProjectAsync(
        string projectPath,
        string outputDirectory)
    {
        var decompiler = new CSharpProjectDecompiler();
        var result = await decompiler.DecompileAsync(projectPath, outputDirectory);

        foreach (var file in result.Files)
        {
            Console.WriteLine($"Generated: {file.OutputPath}");
        }

        foreach (var diagnostic in result.Diagnostics)
        {
            Console.Error.WriteLine(
                $"{diagnostic.FilePath}({diagnostic.Line},{diagnostic.Column}): " +
                $"error {diagnostic.Code}: {diagnostic.Message}");
        }

        return result.Success ? 0 : 1;
    }

    private static async Task<int> BenchmarkFileAsync(
        string csharpPath,
        IEnumerable<string> encodings)
    {
        if (!File.Exists(csharpPath))
        {
            Console.Error.WriteLine(
                $"C# file not found: {csharpPath}");
            return 1;
        }

        var source = await File.ReadAllTextAsync(csharpPath);

        TinyTokenBenchmarkResult result;

        try
        {
            result = new TinyTokenBenchmark().Benchmark(
                source,
                csharpPath,
                encodings);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Token benchmark failed: {ex.Message}");
            return 1;
        }

        if (!result.Success)
        {
            foreach (var diagnostic in result.Diagnostics)
            {
                Console.Error.WriteLine(
                    $"{csharpPath}({diagnostic.Line},{diagnostic.Column}): " +
                    $"error {diagnostic.Code}: {diagnostic.Message}");
            }

            return 1;
        }

        Console.WriteLine($"Token benchmark: {csharpPath}");
        Console.WriteLine(
            $"Characters: source={result.SourceCharacters}, " +
            $"canonical-csharp={result.CanonicalCSharpCharacters}, " +
            $"tiny={result.TinyCharacters}");
        Console.WriteLine();
        Console.WriteLine(
            $"{"Encoding",-14} {"Source",8} {"Canonical",10} {"Tiny",8} " +
            $"{"vs source",10} {"vs canonical",13}");

        foreach (var encoding in result.Encodings)
        {
            Console.WriteLine(
                $"{encoding.Encoding,-14} " +
                $"{encoding.SourceCSharpTokens,8} " +
                $"{encoding.CanonicalCSharpTokens,10} " +
                $"{encoding.TinyTokens,8} " +
                $"{encoding.ReductionVsSourcePercent,9:F1}% " +
                $"{encoding.ReductionVsCanonicalPercent,12:F1}%");
        }

        return 0;
    }

    private static async Task<int> BenchmarkProjectAsync(
        string projectPath,
        IEnumerable<string> encodings)
    {
        if (!File.Exists(projectPath))
        {
            Console.Error.WriteLine(
                $"Project not found: {projectPath}");
            return 1;
        }

        TinyProjectTokenBenchmarkResult result;

        try
        {
            result = await new TinyProjectTokenBenchmark().BenchmarkAsync(
                projectPath,
                encodings);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Project token benchmark failed: {ex.Message}");
            return 1;
        }

        if (!result.Success)
        {
            Console.Error.WriteLine(
                "No supported C# files could be benchmarked.");

            foreach (var diagnostic in result.Diagnostics)
            {
                Console.Error.WriteLine(
                    $"{diagnostic.FilePath}({diagnostic.Line},{diagnostic.Column}): " +
                    $"error {diagnostic.Code}: {diagnostic.Message}");
            }

            return 1;
        }

        Console.WriteLine(
            $"Project token benchmark: {projectPath}");
        Console.WriteLine(
            $"Files: benchmarked={result.Files.Count}, " +
            $"skipped={result.SkippedSourceFiles.Count}");
        Console.WriteLine(
            $"Characters: source={result.SourceCharacters}, " +
            $"canonical-csharp={result.CanonicalCSharpCharacters}, " +
            $"tiny={result.TinyCharacters}");
        Console.WriteLine();
        Console.WriteLine(
            $"{"Encoding",-14} {"Source",8} {"Canonical",10} {"Tiny",8} " +
            $"{"vs source",10} {"vs canonical",13} {"median",9} {"worst",9}");

        foreach (var encoding in result.Encodings)
        {
            Console.WriteLine(
                $"{encoding.Encoding,-14} " +
                $"{encoding.SourceCSharpTokens,8} " +
                $"{encoding.CanonicalCSharpTokens,10} " +
                $"{encoding.TinyTokens,8} " +
                $"{encoding.ReductionVsSourcePercent,9:F1}% " +
                $"{encoding.ReductionVsCanonicalPercent,12:F1}% " +
                $"{encoding.MedianReductionVsCanonicalPercent,8:F1}% " +
                $"{encoding.WorstReductionVsCanonicalPercent,8:F1}%");

            if (!string.IsNullOrWhiteSpace(encoding.WorstFile))
            {
                Console.WriteLine(
                    $"  worst file: {encoding.WorstFile}");
            }
        }

        if (!result.Complete)
        {
            Console.WriteLine();
            Console.WriteLine(
                "Skipped unsupported files:");

            foreach (var path in result.SkippedSourceFiles)
            {
                Console.WriteLine($"  {path}");
            }
        }

        return 0;
    }

    private static int InvalidUsage()
    {
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  tinycs compile <project.csproj>");
        Console.Error.WriteLine("  tinycs decompile <source.cs> [output.tcs]");
        Console.Error.WriteLine("  tinycs decompile-project <project.csproj> <output-directory>");
        Console.Error.WriteLine("  tinycs benchmark <source.cs> [encoding ...]");
        Console.Error.WriteLine("  tinycs benchmark-project <project.csproj> [encoding ...]");
    }
}
