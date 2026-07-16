# Tiny.CSharp Compiler

A proof-of-concept compiler for a minimal C#-like language that generates C# source files.

## Overview

Tiny.CSharp is a source-to-source compiler that transforms `.tcs` files (Tiny.CSharp files) into standard C# files. This allows developers to write concise class declarations that are then compiled into full C# code.

## Features

- Compile `.tcs` files to `.cs` files
- Support for public sealed class declarations with `psc` keyword
- Property type inference and aliases (i, s, g, etc.)
- Property access modes (get/set, get/init, get/private set)
- Namespace inference from project structure
- Explicit namespace and using directives
- MSBuild integration for automatic compilation during build

## Usage

### Direct Compilation

```bash
dotnet run --project src/TinyCSharp.Compiler -- compile samples/TinyCSharp.Sample/TinyCSharp.Sample.csproj
```

### MSBuild Integration

The compiler integrates with .NET projects via MSBuild targets. Simply add a `.tcs` file to your project and it will be automatically compiled during the build process.

## Syntax

### Class Declaration

```tinycs
psc ClassName => Property1,Property2:Type|Mode
```

### Property Modes

- `0` (default): `get; set;`
- `1`: `get; init;`
- `2`: `get; private set;`

### Primitive Type Aliases

| Alias | Type |
|-------|------|
| s | string |
| i | int |
| l | long |
| b | bool |
| d | double |
| m | decimal |
| f | float |
| c | char |
| by | byte |
| dt | DateTime |
| g | Guid |
| o | object |

### Namespace Directive

```tinycs
n:MyCompany.MyNamespace
psc MyClass => Property
```

### Using Directive

```tinycs
u:System.Collections.Generic
psc MyClass => Property
```

## Project Structure

```
Tiny.CSharp/
├── src/
│   └── TinyCSharp.Compiler/          # The compiler implementation
├── samples/
│   └── TinyCSharp.Sample/            # Sample project
├── build/
│   └── TinyCSharp.Build.targets      # MSBuild integration
├── .github/workflows/ci.yml          # CI workflow
└── Directory.Build.props             # Automatic MSBuild target import
```

## Development

To develop the compiler:

1. Run `dotnet build` in the `src/TinyCSharp.Compiler` directory
2. Test with sample files in `samples/TinyCSharp.Sample/`
3. Run `dotnet test` in the `tests/TinyCSharp.Compiler.Tests` directory

## License

MIT