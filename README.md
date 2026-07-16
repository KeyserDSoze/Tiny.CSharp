# Tiny.CSharp

**Write less syntax. Generate real C#.**

Tiny.CSharp is a compact, C#-compatible source language designed to reduce the
syntax and boilerplate required to express most C# constructs. Tiny.CSharp files
use the `.tcs` extension and are compiled into readable `.g.cs` files, which are
then compiled by the standard .NET toolchain.

The long-term goal is broad C# coverage while keeping method implementations
close to normal C# where compression would reduce clarity.

## Foundation proof of concept

The first feature establishes a working compiler, CLI, MSBuild integration,
unit tests, end-to-end tests, a sample project, CI, and structured documentation.

Tiny.CSharp input:

```tinycs
psc Match => Id,Name,AnotherNumber:i,Number:i|1,Code:i|2
```

Generated C#:

```csharp
public sealed class Match
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int AnotherNumber { get; set; }
    public int Number { get; init; }
    public int Code { get; private set; }
}
```

## Build

The repository requires the .NET 10 SDK.

```bash
dotnet restore Tiny.CSharp.sln
dotnet build Tiny.CSharp.sln --configuration Release --no-restore
dotnet test Tiny.CSharp.sln --configuration Release --no-build
```

## Compiler CLI

```bash
dotnet run --project src/TinyCSharp.Compiler -- compile path/to/project
```

The command recursively compiles `.tcs` files and writes sibling `.g.cs` files.

## Documentation

Start with the [documentation index](docs/README.md).

- [Foundation functional analysis](docs/features/foundation.md)
- [Foundation language syntax](docs/language/foundation-syntax.md)
- [Compiler pipeline and build integration](docs/architecture/compiler-pipeline.md)

## Current status

Tiny.CSharp is an early proof of concept. The Foundation syntax is deliberately
small and exists to validate the architecture before expanding toward broader
C# language coverage.
