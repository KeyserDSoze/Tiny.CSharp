using TinyCSharp.Compiler.Decompilation;
using TinyCSharp.Compiler.Parsing;

namespace TinyCSharp.Compiler.Benchmarking;

public sealed class TinyProjectTokenBenchmark
{
    public async Task<TinyProjectTokenBenchmarkResult> BenchmarkAsync(
        string projectPath,
        IEnumerable<string>? encodings = null,
        CancellationToken cancellationToken = default)
    {
        projectPath = Path.GetFullPath(projectPath);
        var projectDirectory =
            Path.GetDirectoryName(projectPath) ?? string.Empty;
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "tinycs-token-benchmark-" +
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var decompilation =
                await new CSharpProjectDecompiler().DecompileAsync(
                    projectPath,
                    temporaryDirectory,
                    cancellationToken);
            var benchmark = new TinyTokenBenchmark();
            var selectedEncodings =
                TinyTokenBenchmark.NormalizeEncodings(encodings);
            var files = new List<TinyProjectTokenFileResult>();

            foreach (var converted in decompilation.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var csharpSource = await File.ReadAllTextAsync(
                    converted.SourcePath,
                    cancellationToken);
                var tinySource = await File.ReadAllTextAsync(
                    converted.OutputPath,
                    cancellationToken);
                var document = new TinyParser().Parse(
                    tinySource,
                    converted.OutputPath);

                if (!document.IsValid)
                {
                    continue;
                }

                document.SourceFilePath = converted.OutputPath;

                var result = benchmark.BenchmarkDocument(
                    csharpSource,
                    document,
                    selectedEncodings);

                files.Add(new TinyProjectTokenFileResult(
                    Path.GetRelativePath(
                        projectDirectory,
                        converted.SourcePath),
                    converted.SourcePath,
                    result.SourceCharacters,
                    result.CanonicalCSharpCharacters,
                    result.TinyCharacters,
                    result.Encodings));
            }

            var aggregates = BuildAggregates(
                files,
                selectedEncodings);
            var skippedSourceFiles = decompilation.Diagnostics
                .Select(diagnostic => diagnostic.FilePath)
                .Where(path =>
                    path.EndsWith(
                        ".cs",
                        StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            return new TinyProjectTokenBenchmarkResult(
                files.Count > 0,
                decompilation.Diagnostics.Count == 0,
                files,
                aggregates,
                decompilation.Diagnostics,
                skippedSourceFiles);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(
                    temporaryDirectory,
                    recursive: true);
            }
        }
    }

    private static IReadOnlyList<TinyProjectTokenEncodingResult>
        BuildAggregates(
            IReadOnlyList<TinyProjectTokenFileResult> files,
            IReadOnlyList<string> encodings)
    {
        var aggregates =
            new List<TinyProjectTokenEncodingResult>();

        foreach (var encodingName in encodings)
        {
            var measurements = files
                .Select(file => new
                {
                    File = file,
                    Encoding = file.Encodings.Single(
                        encoding =>
                            string.Equals(
                                encoding.Encoding,
                                encodingName,
                                StringComparison.OrdinalIgnoreCase))
                })
                .ToArray();

            var sourceTokens = measurements.Sum(
                measurement =>
                    measurement.Encoding.SourceCSharpTokens);
            var canonicalTokens = measurements.Sum(
                measurement =>
                    measurement.Encoding.CanonicalCSharpTokens);
            var tinyTokens = measurements.Sum(
                measurement =>
                    measurement.Encoding.TinyTokens);

            var perFileReductions = measurements
                .Select(measurement =>
                    measurement.Encoding
                        .ReductionVsCanonicalPercent)
                .OrderBy(value => value)
                .ToArray();
            var median = CalculateMedian(perFileReductions);
            var worst = measurements
                .OrderBy(measurement =>
                    measurement.Encoding
                        .ReductionVsCanonicalPercent)
                .ThenBy(
                    measurement =>
                        measurement.File.RelativePath,
                    StringComparer.Ordinal)
                .FirstOrDefault();

            aggregates.Add(
                new TinyProjectTokenEncodingResult(
                    encodingName,
                    sourceTokens,
                    canonicalTokens,
                    tinyTokens,
                    CalculateReduction(
                        sourceTokens,
                        tinyTokens),
                    CalculateReduction(
                        canonicalTokens,
                        tinyTokens),
                    median,
                    worst?.Encoding
                        .ReductionVsCanonicalPercent ?? 0,
                    worst?.File.RelativePath));
        }

        return aggregates;
    }

    private static double CalculateMedian(
        IReadOnlyList<double> sorted)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        var middle = sorted.Count / 2;

        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2d;
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

public sealed record TinyProjectTokenBenchmarkResult(
    bool Success,
    bool Complete,
    IReadOnlyList<TinyProjectTokenFileResult> Files,
    IReadOnlyList<TinyProjectTokenEncodingResult> Encodings,
    IReadOnlyList<CSharpProjectDecompilationDiagnostic> Diagnostics,
    IReadOnlyList<string> SkippedSourceFiles)
{
    public int SourceCharacters =>
        Files.Sum(file => file.SourceCharacters);

    public int CanonicalCSharpCharacters =>
        Files.Sum(file => file.CanonicalCSharpCharacters);

    public int TinyCharacters =>
        Files.Sum(file => file.TinyCharacters);
}

public sealed record TinyProjectTokenFileResult(
    string RelativePath,
    string SourcePath,
    int SourceCharacters,
    int CanonicalCSharpCharacters,
    int TinyCharacters,
    IReadOnlyList<TinyTokenEncodingResult> Encodings);

public sealed record TinyProjectTokenEncodingResult(
    string Encoding,
    int SourceCSharpTokens,
    int CanonicalCSharpTokens,
    int TinyTokens,
    double ReductionVsSourcePercent,
    double ReductionVsCanonicalPercent,
    double MedianReductionVsCanonicalPercent,
    double WorstReductionVsCanonicalPercent,
    string? WorstFile);
