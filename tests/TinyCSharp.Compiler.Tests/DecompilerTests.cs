using System.Text.Json;
using TinyCSharp.Compiler.Decompilation;
using TinyCSharp.Compiler.Generation;
using TinyCSharp.Compiler.Parsing;
using Xunit;

namespace TinyCSharp.Compiler.Tests;

public sealed class DecompilerTests
{
    [Fact]
    public void Decompile_ConvertsSupportedClass_ToCanonicalTiny()
    {
        const string source = """
using System;
namespace Example.Domain;

internal sealed class Match
{
    public Guid Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public int Score { get; private set; }
}
""";

        var decompiler = new CSharpDecompiler();
        var result = decompiler.Decompile(source, "Match.cs");

        Assert.True(result.Success);
        Assert.NotNull(result.Document);

        var formatter = new TinyFormatter();
        var tiny = formatter.Format(result.Document!);

        Assert.Equal(
            "n:Example.Domain\n\nisc Match => Id:g|1,Name,Score:i|2\n",
            tiny);
    }

    [Theory]
    [InlineData("public abstract class", "pac")]
    [InlineData("public partial class", "ppc")]
    [InlineData("public partial abstract class", "papc")]
    [InlineData("public sealed partial class", "pspc")]
    [InlineData("internal partial abstract class", "iapc")]
    public void Decompile_ClassModifiers_AreCanonicalized(
        string declaration,
        string expectedToken)
    {
        var source = $"""
{{declaration}} Model
{
    public int Value { get; set; }
}
""";

        var result = new CSharpDecompiler().Decompile(source);

        Assert.True(result.Success);
        Assert.Equal(
            expectedToken,
            result.Document!.TypeDeclaration.ToToken());

        var tiny = new TinyFormatter().Format(result.Document);
        Assert.Equal(
            $"{expectedToken} Model => Value:i\n",
            tiny);
    }

    [Fact]
    public void Decompile_ClassWithoutAccessibility_UsesInternalCanonicalToken()
    {
        const string source = """
class CacheEntry
{
    public int Value { get; set; }
}
""";

        var result = new CSharpDecompiler().Decompile(source);

        Assert.True(result.Success);
        Assert.Equal("ic", result.Document!.TypeDeclaration.ToToken());
    }

    [Fact]
    public void Decompile_CompositionalTypes_UsesRecursiveAliases()
    {
        const string source = """
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class Types
{
    public string? Maybe { get; set; }
    public Guid[] Ids { get; set; }
    public List<string> Names { get; set; }
    public Dictionary<string, List<int>> Map { get; set; }
    public Task<Result> Work { get; init; }
}
""";

        var result = new CSharpDecompiler().Decompile(source);
        Assert.True(result.Success);

        var tiny = new TinyFormatter().Format(result.Document!);

        Assert.Contains("Maybe:s?", tiny);
        Assert.Contains("Ids:g[]", tiny);
        Assert.Contains("Names:List<s>", tiny);
        Assert.Contains("Map:Dictionary<s,List<i>>", tiny);
        Assert.Contains("Work:Task<Result>|1", tiny);
    }

    [Fact]
    public void Decompile_QualifiedGenericType_PreservesQualification()
    {
        const string source = """
public class QualifiedTypes
{
    public System.Collections.Generic.List<string> Names { get; set; }
    public System.Guid Id { get; init; }
}
""";

        var result = new CSharpDecompiler().Decompile(source);
        Assert.True(result.Success);

        var tiny = new TinyFormatter().Format(result.Document!);

        Assert.Contains("Names:System.Collections.Generic.List<s>", tiny);
        Assert.Contains("Id:System.Guid|1", tiny);

        var parsed = new TinyParser().Parse(tiny);
        Assert.True(parsed.IsValid);

        var regenerated = new CSharpGenerator().Generate(parsed);
        Assert.Contains("public System.Collections.Generic.List<string> Names { get; set; }", regenerated);
        Assert.Contains("public System.Guid Id { get; init; }", regenerated);
    }

    [Fact]
    public void Decompile_Semantics_RemoveUnusedUsingsAndKeepRequiredOnes()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading.Tasks;

public class Types
{
    public List<int> Values { get; set; }
}
""";

        var result = new CSharpDecompiler().Decompile(source);
        Assert.True(result.Success);

        var tiny = new TinyFormatter().Format(result.Document!);

        Assert.Contains("u:System.Collections.Generic", tiny);
        Assert.DoesNotContain("u:System.Threading.Tasks", tiny);
        Assert.Contains("Values:List<i>", tiny);
    }

    [Fact]
    public void Decompile_MultiFileSemanticContext_ResolvesProjectTypeUsing()
    {
        var context = CSharpSemanticCompilation.Create(
            new[]
            {
                new CSharpSourceDocument(
                    "Contracts/Result.cs",
                    "namespace Example.Contracts; public class Result { }"),
                new CSharpSourceDocument(
                    "Models/Model.cs",
                    """
using Example.Contracts;
namespace Example.Models;

public class Model
{
    public Result Value { get; set; }
}
""")
            });

        var result = new CSharpDecompiler().Decompile(context, "Models/Model.cs");

        Assert.True(result.Success);
        Assert.Equal("Example.Contracts", result.Document!.Properties[0].Type.ResolvedNamespace);

        var tiny = new TinyFormatter().Format(result.Document);
        Assert.Contains("u:Example.Contracts", tiny);
        Assert.Contains("Value:Result", tiny);
    }

    [Fact]
    public void Decompile_AmbiguousSemanticType_ReportsTcs6003()
    {
        var context = CSharpSemanticCompilation.Create(
            new[]
            {
                new CSharpSourceDocument(
                    "A/Result.cs",
                    "namespace A; public class Result { }"),
                new CSharpSourceDocument(
                    "B/Result.cs",
                    "namespace B; public class Result { }"),
                new CSharpSourceDocument(
                    "Model.cs",
                    """
using A;
using B;

public class Model
{
    public Result Value { get; set; }
}
""")
            });

        var result = new CSharpDecompiler().Decompile(context, "Model.cs");

        Assert.False(result.Success);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TCS6003", diagnostic.Code);
        Assert.Contains("ambiguous", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DecompileProject_UsesTinyGeneratedCsAsSemanticContext_WithoutReEmittingIt()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "tinycs-decompile-" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "tiny-output");

        Directory.CreateDirectory(Path.Combine(root, "Contracts"));
        Directory.CreateDirectory(Path.Combine(root, "Models"));

        try
        {
            var projectPath = Path.Combine(root, "Example.csproj");
            await File.WriteAllTextAsync(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

            var generatedResultPath = Path.Combine(root, "Contracts", "Result.cs");
            await File.WriteAllTextAsync(
                generatedResultPath,
                """
// <auto-generated />
namespace Example.Contracts;
public class Result { }
""");

            await File.WriteAllTextAsync(
                Path.Combine(root, "Contracts", "Result.tcs"),
                "pc Result => ");

            await File.WriteAllTextAsync(
                Path.Combine(root, "Models", "Model.cs"),
                """
using Example.Contracts;
namespace Example.Models;

public class Model
{
    public Result Value { get; set; }
}
""");

            var result = await new CSharpProjectDecompiler().DecompileAsync(
                projectPath,
                output);

            Assert.True(result.Success);
            Assert.Single(result.Files);

            var modelTinyPath = Path.Combine(output, "Models", "Model.tcs");
            Assert.True(File.Exists(modelTinyPath));
            Assert.False(File.Exists(Path.Combine(output, "Contracts", "Result.tcs")));

            var tiny = await File.ReadAllTextAsync(modelTinyPath);
            Assert.Contains("u:Example.Contracts", tiny);
            Assert.Contains("pc Model => Value:Result", tiny);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DecompileProject_ResolvesDirectProjectReference()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "tinycs-decompile-project-ref-" + Guid.NewGuid().ToString("N"));
        var app = Path.Combine(root, "App");
        var contracts = Path.Combine(root, "Contracts");
        var output = Path.Combine(root, "tiny-output");

        Directory.CreateDirectory(app);
        Directory.CreateDirectory(contracts);

        try
        {
            var contractsProject = Path.Combine(contracts, "Contracts.csproj");
            await File.WriteAllTextAsync(
                contractsProject,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            await File.WriteAllTextAsync(
                Path.Combine(contracts, "DirectReferencedContract.cs"),
                "namespace Referenced.Contracts; public class DirectReferencedContract { }");

            var appProject = Path.Combine(app, "App.csproj");
            await File.WriteAllTextAsync(
                appProject,
                """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Contracts/Contracts.csproj" />
  </ItemGroup>
</Project>
""");

            await File.WriteAllTextAsync(
                Path.Combine(app, "Model.cs"),
                """
using Referenced.Contracts;

public class Model
{
    public DirectReferencedContract Value { get; set; }
}
""");

            var result = await new CSharpProjectDecompiler().DecompileAsync(
                appProject,
                output);

            Assert.True(result.Success);
            Assert.Single(result.Files);

            var tinyPath = Path.Combine(output, "Model.tcs");
            Assert.True(File.Exists(tinyPath));

            var tiny = await File.ReadAllTextAsync(tinyPath);
            Assert.Contains("u:Referenced.Contracts", tiny);
            Assert.Contains(
                "pc Model => Value:DirectReferencedContract",
                tiny);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DecompileProject_ResolvesPackageCompileAsset()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "tinycs-decompile-package-" + Guid.NewGuid().ToString("N"));
        var packageRoot = Path.Combine(root, "packages");
        var packageAssemblyDirectory = Path.Combine(
            packageRoot,
            "tiny.fake.package",
            "1.0.0",
            "lib",
            "net10.0");
        var obj = Path.Combine(root, "obj");
        var output = Path.Combine(root, "tiny-output");

        Directory.CreateDirectory(packageAssemblyDirectory);
        Directory.CreateDirectory(obj);

        try
        {
            var compilerAssembly =
                typeof(TinyCSharp.Compiler.Compilation.TinyProjectCompiler)
                    .Assembly
                    .Location;
            var copiedAssembly = Path.Combine(
                packageAssemblyDirectory,
                "TinyCSharp.Compiler.dll");

            File.Copy(
                compilerAssembly,
                copiedAssembly,
                overwrite: true);

            var projectPath = Path.Combine(root, "Example.csproj");
            await File.WriteAllTextAsync(
                projectPath,
                """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Tiny.Fake.Package" Version="1.0.0" />
  </ItemGroup>
</Project>
""");

            var normalizedPackageRoot =
                packageRoot.Replace('\\', '/') + "/";
            var assets = new Dictionary<string, object?>
            {
                ["version"] = 3,
                ["targets"] = new Dictionary<string, object?>
                {
                    ["net10.0"] = new Dictionary<string, object?>
                    {
                        ["Tiny.Fake.Package/1.0.0"] = new Dictionary<string, object?>
                        {
                            ["compile"] = new Dictionary<string, object?>
                            {
                                ["lib/net10.0/TinyCSharp.Compiler.dll"] =
                                    new Dictionary<string, object?>()
                            }
                        }
                    }
                },
                ["libraries"] = new Dictionary<string, object?>
                {
                    ["Tiny.Fake.Package/1.0.0"] = new Dictionary<string, object?>
                    {
                        ["type"] = "package",
                        ["path"] = "tiny.fake.package/1.0.0"
                    }
                },
                ["packageFolders"] = new Dictionary<string, object?>
                {
                    [normalizedPackageRoot] = new Dictionary<string, object?>()
                }
            };

            await File.WriteAllTextAsync(
                Path.Combine(obj, "project.assets.json"),
                JsonSerializer.Serialize(assets));

            await File.WriteAllTextAsync(
                Path.Combine(root, "Model.cs"),
                """
using TinyCSharp.Compiler.Compilation;

public class Model
{
    public TinyProjectCompiler Compiler { get; set; }
}
""");

            var result = await new CSharpProjectDecompiler().DecompileAsync(
                projectPath,
                output);

            Assert.True(result.Success);
            var tiny = await File.ReadAllTextAsync(
                Path.Combine(output, "Model.tcs"));

            Assert.Contains("u:TinyCSharp.Compiler.Compilation", tiny);
            Assert.Contains(
                "pc Model => Compiler:TinyProjectCompiler",
                tiny);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Decompile_UnsupportedConstruct_ReportsStableDiagnosticCode()
    {
        const string source = """
public class User
{
    public void Save() { }
}
""";

        var result = new CSharpDecompiler().Decompile(source);

        Assert.False(result.Success);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TCS6002", diagnostic.Code);
        Assert.True(diagnostic.Line > 0);
        Assert.True(diagnostic.Column > 0);
    }

    [Fact]
    public void Decompile_RejectsUnsupportedMembers_InsteadOfDroppingThem()
    {
        const string source = """
public class User
{
    public string Name { get; set; } = string.Empty;
    public void Save() { }
}
""";

        var result = new CSharpDecompiler().Decompile(source);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("Member kind", StringComparison.Ordinal));
    }

    [Fact]
    public void Decompile_RejectsStringGetSetWithoutEmptyInitializer()
    {
        const string source = """
public class User
{
    public string Name { get; set; }
}
""";

        var result = new CSharpDecompiler().Decompile(source);

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Message.Contains("string.Empty", StringComparison.Ordinal));
    }

    [Fact]
    public void RoundTrip_CSharp_ToTiny_ToCSharp_PreservesSupportedSemantics()
    {
        const string source = """
namespace Example;

public sealed class User
{
    public string Name { get; set; } = "";
    public int Age { get; init; }
}
""";

        var decompiled = new CSharpDecompiler().Decompile(source, "User.cs");
        Assert.True(decompiled.Success);

        var tiny = new TinyFormatter().Format(decompiled.Document!);
        Assert.Equal("n:Example\n\npsc User => Name,Age:i|1\n", tiny);

        var parsed = new TinyParser().Parse(tiny);
        Assert.True(parsed.IsValid);

        parsed.SourceFilePath = "User.tcs";
        var regenerated = new CSharpGenerator().Generate(parsed);

        Assert.Contains("namespace Example;", regenerated);
        Assert.Contains("public sealed class User", regenerated);
        Assert.Contains("public string Name { get; set; } = string.Empty;", regenerated);
        Assert.Contains("public int Age { get; init; }", regenerated);
    }
}
