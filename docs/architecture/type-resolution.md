# Type resolution

Tiny.CSharp resolves simple named property types at project scope before generating
C#.

## Pipeline

```text
discover .tcs
  -> parse every file
  -> infer/accept namespaces
  -> create Tiny declaration stubs
  -> load handwritten project C#
  -> build Roslyn compilation
  -> index current-project and framework types
  -> resolve every named Tiny type
  -> add imports or qualification
  -> generate sibling .cs
```

Stale/generated sibling C# owned by a `.tcs` file is excluded from the handwritten
C# input. The corresponding Tiny declaration stub represents that type instead, so
old generated output cannot influence resolution.

## Candidate information

The resolver indexes:

- simple name;
- fully qualified name;
- namespace;
- generic arity;
- Roslyn type kind;
- accessibility;
- source kind (`Tiny`, handwritten `CSharp`, or `Framework`);
- containing assembly identity.

Only accessible named types are indexed. Current-project `public` and `internal`
types are eligible; referenced framework types must be public.

## Deterministic priority

For a Tiny document, candidates are ordered by:

1. same namespace;
2. namespace listed by an explicit `u:`;
3. Tiny.CSharp source;
4. handwritten C# source;
5. framework source;
6. namespace ordinal order;
7. qualified-name ordinal order;
8. assembly identity ordinal order.

Generic arity must match. For example, `List<s>` resolves only against named types
called `List` with arity 1.

## TCS2001: ambiguity

More than one accessible candidate produces a warning. The warning lists candidates
and the selected type. The generated property is fully qualified to ensure that the
deterministic selection remains valid C# even when conflicting imports exist.

## TCS2002: unresolved name

No candidate produces a warning, not an error. Tiny.CSharp leaves the name unchanged
in generated C#, preserving Roslyn as the final authority.

## Automatic imports

A unique selected type in another namespace adds a deterministic using directive.
When `EmitAutomaticUsings` is disabled, the selected type is fully qualified
instead.

## Current boundary

The symbol compilation currently contains:

- all valid Tiny.CSharp project declarations as stubs;
- handwritten C# from the current project;
- trusted platform assemblies.

The next layer is the evaluated .NET project graph: direct/transitive project
references and NuGet/external assembly references.
