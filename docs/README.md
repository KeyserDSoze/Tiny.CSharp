# Tiny.CSharp documentation

Tiny.CSharp is a compact, canonical, and reversible representation of supported C#
constructs.

## Architecture

- [Round-trip architecture](architecture/round-trip.md) — shared model, Roslyn
  decompilation, canonical formatting, and safety rules.
- [Type resolution](architecture/type-resolution.md) — project symbol indexing,
  deterministic candidate priority, automatic imports, and diagnostics.

## Benchmarking

- [Token efficiency](benchmarking/token-efficiency.md) — deterministic tokenizer
  baselines and the acceptance policy for new shorthand.

## Language contract

The executable language profile is documented in the root [README](../README.md).
The LLM-facing authoritative contract lives in [prompts/system.md](../prompts/system.md).

The Foundation issue tracks the broader target; implementation support should not
be inferred from the issue until the corresponding syntax, tests, and compiler
code land in the repository.
