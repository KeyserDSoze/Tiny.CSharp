using System;
using System.IO;
using System.Threading.Tasks;
using TinyCSharp.Compiler.Compilation;

namespace TinyCSharp.Compiler;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 || !string.Equals(args[0], "compile", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Usage: tinycs compile <project.csproj>");
            return 1;
        }

        var projectPath = args[1];
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
            Console.WriteLine($"{diagnostic.FilePath}({diagnostic.Line},{diagnostic.Column}): {diagnostic.Severity} {diagnostic.Message}");
        }

        return 1;
    }
}
