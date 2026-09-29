using System.Collections.Generic;
using System.Threading.Tasks;

namespace TinyCSharp.BenchmarkCorpus.Contracts;

internal sealed class ProcessingEnvelope
{
    public Guid Id { get; init; }
    public Dictionary<string, List<int>> Values { get; set; }
    public Task<ProcessingResult> Result { get; init; }
}

public class ProcessingResult
{
}
