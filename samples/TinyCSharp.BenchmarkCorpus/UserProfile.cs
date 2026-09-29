namespace TinyCSharp.BenchmarkCorpus.Models;

public sealed class UserProfile
{
    public string Identifier { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Age { get; set; }
    public Guid CorrelationId { get; init; }
}
