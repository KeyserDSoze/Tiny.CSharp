using Microsoft.ML.Tokenizers;
using TinyCSharp.Compiler.Decompilation;
using TinyCSharp.Compiler.Generation;
using TinyCSharp.Compiler.Language;

namespace TinyCSharp.Compiler.Benchmarking;

public sealed class TinyTokenBenchmark
{
    public static IReadOnlyList<string> DefaultEncodings { get; } =
        new[]
        {
            "o200k_base",
            "cl100k_base"
        };

    private readonly Dictionary<string, Tokenizer> _tokenizers =
        new(StringComparer.OrdinalIgnoreCase);

    public TinyTokenBenchmarkResult Benchmark(
        string csharpSource,
        string sourceFilePath = "",
        IEnumerable<string>? encodings = null)
    {
        var decompiled = new CSharpDecompiler().Decompile(
            csharpSource,
            sourceFilePath);

        if (!decompiled.Success || decompiled.Document is null)
        {
            return new TinyTokenBenchmarkResult(
                false,
                csharpSource,
                string.Empty,
                string.Empty,
                Array.Empty<TinyTokenEncodingResult>(),
                decompiled.Diagnostics);
        }

        return BenchmarkDocument(
            csharpSource,
            decompiled.Document,
            encodings);
    }

    public TinyTokenBenchmarkResult BenchmarkDocument(
        string csharpSource,
        TinyDocument document,
        IEnumerable<string>? encodings = null)
    {
        var tinySource = new TinyFormatter().Format(document);
        var canonicalCSharp = new CSharpGenerator().Generate(
            document,
            includeGeneratedHeader: false);
        var selectedEncodings = NormalizeEncodings(encodings);
        var results = new List<TinyTokenEncodingResult>();

        foreach (var encoding in selectedEncodings)
        {
            var tokenizer = GetTokenizer(encoding);
            var sourceTokens = tokenizer.CountTokens(csharpSource);
            var canonicalTokens = tokenizer.CountTokens(canonicalCSharp);
            var tinyTokens = tokenizer.CountTokens(tinySource);

            results.Add(new TinyTokenEncodingResult(
                encoding,
                sourceTokens,
                canonicalTokens,
                tinyTokens,
                CalculateReduction(sourceTokens, tinyTokens),
                CalculateReduction(canonicalTokens, tinyTokens)));
        }

        return new TinyTokenBenchmarkResult(
            true,
            csharpSource,
            canonicalCSharp,
            tinySource,
            results,
            Array.Empty<TinyDecompilationDiagnostic>());
    }

    public static IReadOnlyList<string> NormalizeEncodings(
        IEnumerable<string>? encodings)
    {
        var selected = (encodings ?? DefaultEncodings)
            .Where(encoding => !string.IsNullOrWhiteSpace(encoding))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return selected.Length == 0
            ? DefaultEncodings.ToArray()
            : selected;
    }

    private Tokenizer GetTokenizer(string encoding)
    {
        if (_tokenizers.TryGetValue(
                encoding,
                out var tokenizer))
        {
            return tokenizer;
        }

        tokenizer = TiktokenTokenizer.CreateForEncoding(
            encoding);
        _tokenizers[encoding] = tokenizer;
        return tokenizer;
    }

    private static double CalculateReduction(
        int baselineTokens,
        int tinyTokens)
    {
        if (baselineTokens == 0)
        {
            return 0;
        }

        return (baselineTokens - tinyTokens) *
               100d /
               baselineTokens;
    }
}

public sealed record TinyTokenBenchmarkResult(
    bool Success,
    string SourceCSharp,
    string CanonicalCSharp,
    string TinySource,
    IReadOnlyList<TinyTokenEncodingResult> Encodings,
    IReadOnlyList<TinyDecompilationDiagnostic> Diagnostics)
{
    public int SourceCharacters => SourceCSharp.Length;
    public int CanonicalCSharpCharacters => CanonicalCSharp.Length;
    public int TinyCharacters => TinySource.Length;
}

public sealed record TinyTokenEncodingResult(
    string Encoding,
    int SourceCSharpTokens,
    int CanonicalCSharpTokens,
    int TinyTokens,
    double ReductionVsSourcePercent,
    double ReductionVsCanonicalPercent);
