using System.Collections.Generic;

namespace TinyCSharp.BenchmarkCorpus.Models;

public class MatchSnapshot
{
    public string Name { get; set; } = string.Empty;
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
    public List<string> PlayerNames { get; init; }
}
