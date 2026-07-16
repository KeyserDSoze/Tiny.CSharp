using System.Diagnostics;
using TinyCSharp.Compiler.Compilation;
using Xunit;

namespace TinyCSharp.IntegrationTests;

public sealed class EndToEndTests
{
    [Fact]
    public async Task GeneratesSource_ThenBuilds_Project_Successfully()
    {
        var root = Path.Combine(Path.GetTempPath(), "tinycs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var projectPath = Path.Combine(root, "Tiny.Sample.csproj");
            await File.WriteAllTextAsync(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <RootNamespace>Tiny.Sample</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
""");

            await File.WriteAllTextAsync(Path.Combine(root, "Program.cs"), "using System;\nusing Tiny.Sample;\nConsole.WriteLine(new Match().GetType().Name);");
            await File.WriteAllTextAsync(Path.Combine(root, "Match.tcs"), "psc Match => Id,Name");

            var compiler = new TinyProjectCompiler();
            var result = await compiler.CompileAsync(projectPath);

            Assert.True(result.Success);
            Assert.True(File.Exists(Path.Combine(root, "Match.cs")));

            var build = await RunDotnetAsync($"build \"{projectPath}\"", root);

            Assert.True(build.ExitCode == 0, $"build failed\nSTDOUT:\n{build.StandardOutput}\nSTDERR:\n{build.StandardError}");
            Assert.Contains("Build succeeded", build.StandardOutput);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunDotnetAsync(string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, stdout, stderr);
    }
}
