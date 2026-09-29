# Round-trip architecture

Tiny.CSharp is built around one canonical language model rather than two unrelated
text converters.

```text
C# source
  -> Roslyn syntax tree
  -> CSharpDecompiler
  -> TinyDocument
  -> TinyFormatter
  -> .tcs

.tcs
  -> TinyParser
  -> TinyDocument
  -> CSharpGenerator
  -> .cs
```

## Why a shared model matters

The declaration codec, primitive aliases, class name, namespace, using directives,
and properties live in the same Tiny.CSharp model regardless of which direction
the source came from. This prevents the C# decompiler and Tiny parser from growing
different interpretations of the language.

## Safety rule

C# is compressed only when the current Tiny.CSharp profile can preserve its
supported semantics. Unsupported information is an error; it is never silently
discarded.

The first decompiler profile supports:

- one top-level class;
- `public` or `internal` accessibility, including implicit internal;
- optional `sealed`;
- public instance auto-properties;
- `get; set;`, `get; init;`, and `get; private set;`;
- primitive aliases and simple named types;
- nullable element/value types such as `string?` and `int?`;
- one-dimensional and jagged arrays;
- recursively nested generic property types;
- a single file-scoped or block namespace;
- ordinary using directives.

Examples of constructs deliberately rejected for now include attributes, methods,
fields, constructors, generic classes, base types, interfaces, multidimensional
arrays, nullable array references that cannot be represented losslessly, property
bodies, using aliases, global usings, and static usings.

## String default invariant

The current Tiny.CSharp default string property:

```tinycs
Name
```

expands to:

```csharp
public string Name { get; set; } = string.Empty;
```

Therefore C# -> Tiny.CSharp accepts a `string` get/set property only when its
initializer is semantically the supported empty-string default (`string.Empty`
or `""`). This avoids changing initialization behavior during round trips.

## Next architectural step

The next major expansion should use Roslyn semantic information to resolve named
types and then extend the canonical model for richer type syntax. Syntax should be
added to the language contract before either converter starts emitting it.


## Stable diagnostics

Diagnostics are part of the compiler contract, not incidental text. Parser errors
use stable `TCS1xxx` codes and calculate 1-based source line/column positions.
Decompiler syntax/coverage errors use `TCS6xxx`. Output and project failures use
`TCS3xxx` and `TCS4xxx` respectively.
