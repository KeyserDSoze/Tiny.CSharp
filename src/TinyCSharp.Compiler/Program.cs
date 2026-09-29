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

        if (result.Success)
        {
            Console.WriteLine("Compilation succeeded!");

            foreach (var file in result.Files)
            {
                if (file.Success)
                {
                    Console.WriteLine($"Generated: {file.FilePath.Substring(0, file.FilePath.Length - 4)}.cs");
                }
            }

            return 0;
        }

        Console.WriteLine("Compilation failed!");

        foreach (var diagnostic in result.Diagnostics)
        {
            Console.WriteLine(
                $"{diagnostic.FilePath}({diagnostic.Line},{diagnostic.Column}): " +
                $"{diagnostic.Severity} {diagnostic.Message}");
        }

        return 1;
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
                    $"{csharpPath}({diagnostic.Line},{diagnostic.Column}): error {diagnostic.Message}");
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
    }
}
