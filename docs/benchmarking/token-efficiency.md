# Token-efficiency benchmarking

Tiny.CSharp is intended to reduce LLM context usage, so syntax changes are evaluated
with tokenizers rather than character count alone.

## Baselines

For a supported C# input, the benchmark records three representations:

1. original source C#;
2. canonical C# regenerated from the Tiny semantic model;
3. canonical Tiny.CSharp.

Comparing Tiny with original source measures practical source-context savings.
Comparing Tiny with canonical C# isolates the language representation itself from
comments, whitespace, and formatting differences.

## Encodings

The default deterministic corpus is currently measured with:

- `o200k_base`;
- `cl100k_base`.

The compiler pins the Microsoft Tiktoken tokenizer/data package versions so the same
repository revision produces reproducible counts.

Model names are deliberately not the benchmark identity. A provider may change a
model-to-encoding mapping over time; an encoding name plus package version is a more
stable measurement coordinate.

## CLI

```bash
tinycs benchmark <source.cs> [encoding ...]
```

Example:

```bash
tinycs benchmark Match.cs o200k_base cl100k_base
```

The report includes:

- character counts;
- source C# token count;
- canonical C# token count;
- Tiny.CSharp token count;
- percentage reduction versus source C#;
- percentage reduction versus canonical C#.

## Syntax-change policy

A proposed abbreviation should not be accepted merely because its textual spelling
is shorter.

For changes intended primarily as token compression:

1. implement the candidate syntax canonically;
2. benchmark it on representative C# constructs;
3. compare both default encodings;
4. record any regressions as well as improvements;
5. prefer compositional syntax that improves repeated real-world structures;
6. reject aliases whose readability/grammar cost is not justified by measured token
   savings.

No universal percentage threshold is fixed yet. The benchmark corpus needs to grow
before a threshold can be statistically meaningful.

## Project/corpus benchmark

Project benchmarking is implemented as:

```bash
tinycs benchmark-project <project.csproj> [encoding ...]
```

It reuses project-aware C# decompilation, benchmarks every supported handwritten C#
file, and reports:

- aggregate source/canonical/Tiny token counts;
- aggregate percentage reductions;
- median per-file reduction versus canonical C#;
- worst per-file reduction and file path;
- skipped unsupported files and their decompiler diagnostics.

The repository includes `samples/TinyCSharp.BenchmarkCorpus`, which is executed
by CI. This corpus is the initial evidence base for deciding whether common generic
container names such as `List`, `Dictionary`, and `Task` deserve dedicated
Tiny aliases. The corpus should grow before such aliases are standardized.
