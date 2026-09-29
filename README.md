# Tiny.CSharp

**A compact, reversible C# representation designed for LLMs and the .NET toolchain.**

Tiny.CSharp (`.tcs`) is an experimental source language that compresses common C#
syntax into a deterministic form, then expands it back into normal C#.

The goal is not to create a different runtime or a different type system. The goal
is to represent C# with less repetitive syntax, fewer characters, and eventually
fewer LLM tokens while preserving a predictable path back to standard C#.

> Token efficiency must be measured with real model tokenizers. Shorter source is
> a design goal, but character count alone is not treated as proof of token savings.

## Core design rules

Tiny.CSharp is being designed around five rules:

1. **Canonical syntax** — one preferred Tiny.CSharp representation for a supported
   C# construct. No synonyms.
2. **Composable abbreviations** — compact declaration codes are built from ordered
   parts instead of introducing a new keyword for every combination.
3. **Deterministic round trips** — supported C# should be convertible to Tiny.CSharp
   and back without changing its meaning.
4. **C# remains the authority** — generated source is ordinary C# and Roslyn remains
   the final compiler.
5. **No invented shorthand** — syntax is added to the language specification before
   compilers, decompilers, or LLMs are expected to emit it.

## Current language profile

The current compiler supports a deliberately small class/property subset. It is a
foundation for the broader compiler + decompiler architecture.

### Compact type declarations

Type declaration codes follow this canonical order:

```text
<accessibility><modifiers><kind>
```

The current profile assigns:

| Part | Tiny | C# |
|---|---:|---|
| accessibility | `p` | `public` |
| accessibility | `i` | `internal` |
| modifier | `s` | `sealed` |
| kind | `c` | `class` |

That makes the currently supported declarations:

| Tiny.CSharp | C# |
|---|---|
| `pc` | `public class` |
| `psc` | `public sealed class` |
| `ic` | `internal class` |
| `isc` | `internal sealed class` |

For example:

```tinycs
isc Match => Id,Name,Number:i|1
```

expands to:

```csharp
internal sealed class Match
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Number { get; init; }
}
```

The same composition rule is intended to scale to additional accessibility,
modifier, and type-kind codes. New codes are not considered part of the language
until they are documented and implemented.

### Properties

The current property grammar is:

```text
PropertyName[:Type][|Mode]
```

Defaults:

```text
Type omitted  -> string
Mode omitted  -> 0
```

Accessor modes:

| Mode | Generated C# |
|---:|---|
| omitted / `0` | `{ get; set; }` |
| `1` | `{ get; init; }` |
| `2` | `{ get; private set; }` |

Primitive aliases:

| Tiny | C# |
|---|---|
| `s` | `string` |
| `i` | `int` |
| `l` | `long` |
| `b` | `bool` |
| `d` | `double` |
| `m` | `decimal` |
| `f` | `float` |
| `c` | `char` |
| `by` | `byte` |
| `dt` | `DateTime` |
| `g` | `Guid` |
| `o` | `object` |

Aliases are contextual. For example, `c` is the type-kind code when it appears
at the end of a compact type declaration, and `char` when used as a property
type alias.

### Namespace and using directives

```tinycs
n:MyCompany.Domain
u:System.Collections.Generic
u:MyCompany.Contracts

psc Match => Result:MatchResult
```

If `n:` is omitted, the compiler infers a namespace from the project root
namespace and the relative directory containing the `.tcs` file.

## Compiler

Compile all `.tcs` files in a project with:

```bash
dotnet run --project src/TinyCSharp.Compiler -- compile path/to/project.csproj
```

Each `Name.tcs` owns the sibling `Name.cs`. Generation is performed through a
temporary file and the sibling C# file is replaced only after Tiny.CSharp parsing
and generation succeed.

The repository also contains MSBuild integration in
`build/TinyCSharp.Build.targets`, so the sample project can be built normally.

## Compiler + decompiler direction

Tiny.CSharp is intended to become bidirectional:

```text
C# source
   |
   | Roslyn parser / semantic model
   v
Canonical Tiny.CSharp model
   ^
   | Tiny parser
   |
Tiny.CSharp source

Canonical model -> C# generator
Canonical model -> Tiny.CSharp formatter
```

The important architectural point is that C# -> Tiny.CSharp should not be a set of
regular-expression replacements. Roslyn should parse C#, and both directions should
share the same canonical language model and token tables.

A round trip is judged by semantic equivalence for the supported subset, not by
reproducing the original whitespace or formatting.

## Current implementation versus Foundation specification

The repository's Foundation issue describes a larger target than the implementation
currently provides. Today the project already has:

- `.tcs` discovery and sibling `.cs` generation;
- namespace inference plus explicit `n:`;
- explicit `u:` directives;
- primitive property aliases and accessor modes;
- compact public/internal class declarations with optional `sealed`;
- unit and end-to-end tests;
- MSBuild integration and GitHub Actions.

Important Foundation work still to be completed includes project-wide symbol/type
resolution, stable `TCSxxxx` diagnostics with accurate locations, validation of
namespace/using directives, richer C# type syntax, broader test coverage, and the
C# -> Tiny.CSharp decompiler.

## LLM system prompt

The authoritative LLM contract lives in:

```text
prompts/system.md
```

That prompt intentionally mirrors only syntax that is part of the compiler profile.
When the language grows, the compiler, tests, README, and prompt should change in
the same feature.

## Repository structure

```text
Tiny.CSharp/
├── build/                         # MSBuild integration
├── docs/                          # Language and architecture documentation
├── prompts/                       # LLM language contract
├── samples/                       # Example .NET projects and .tcs files
├── src/TinyCSharp.Compiler/
│   ├── Compilation/
│   ├── Generation/
│   ├── Language/                  # Canonical language tokens/models
│   └── Parsing/
└── tests/
    ├── TinyCSharp.Compiler.Tests/
    └── TinyCSharp.IntegrationTests/
```

## Development

The repository targets .NET 10.

```bash
dotnet restore Tiny.CSharp.sln
dotnet build Tiny.CSharp.sln
dotnet test Tiny.CSharp.sln
```

## License

MIT
