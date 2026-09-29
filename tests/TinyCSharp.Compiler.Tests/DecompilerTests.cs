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
            "n:Example.Domain\nu:System\n\nisc Match => Id:g|1,Name,Score:i|2\n",
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
