using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TinyCSharp.Compiler.Decompilation;

public sealed record CSharpSourceDocument(
    string FilePath,
    string Source);

public sealed class CSharpSemanticCompilation
{
    private static readonly Lazy<IReadOnlyList<MetadataReference>> PlatformReferences =
        new(CreatePlatformReferences);

    private readonly CSharpCompilation _compilation;
    private readonly IReadOnlyDictionary<string, SyntaxTree> _trees;

    private CSharpSemanticCompilation(
        CSharpCompilation compilation,
        IReadOnlyDictionary<string, SyntaxTree> trees)
    {
        _compilation = compilation;
        _trees = trees;
    }

    public static CSharpSemanticCompilation Create(
        IEnumerable<CSharpSourceDocument> sources)
    {
        var trees = new Dictionary<string, SyntaxTree>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            var path = string.IsNullOrWhiteSpace(source.FilePath)
                ? $"source-{trees.Count}.cs"
                : source.FilePath;

            if (trees.ContainsKey(path))
            {
                throw new ArgumentException(
                    $"A C# source document with path '{path}' was supplied more than once.",
                    nameof(sources));
            }

            trees[path] = CSharpSyntaxTree.ParseText(
                source.Source,
                new CSharpParseOptions(LanguageVersion.Preview),
                path);
        }

        var compilation = CSharpCompilation.Create(
            "TinyCSharp.Decompilation",
            trees.Values,
            PlatformReferences.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return new CSharpSemanticCompilation(compilation, trees);
    }

    public SyntaxTree GetSyntaxTree(string filePath)
    {
        if (!_trees.TryGetValue(filePath, out var tree))
        {
            throw new KeyNotFoundException(
                $"The semantic compilation does not contain '{filePath}'.");
        }

        return tree;
    }

    public SemanticModel GetSemanticModel(string filePath)
    {
        return _compilation.GetSemanticModel(GetSyntaxTree(filePath), ignoreAccessibility: true);
    }

    private static IReadOnlyList<MetadataReference> CreatePlatformReferences()
    {
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;

        if (string.IsNullOrWhiteSpace(trustedAssemblies))
        {
            return new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            };
        }

        return trustedAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(MetadataReference.CreateFromFile)
            .ToArray();
    }
}
