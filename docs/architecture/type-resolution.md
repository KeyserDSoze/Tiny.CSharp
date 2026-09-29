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
  -> load recursive ProjectReference graph
  -> load restored package/HintPath metadata
  -> build Roslyn compilations
  -> index current-project, referenced-project, external, and framework types
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
- source kind (`Tiny`, handwritten `CSharp`, direct/transitive project,
  direct/transitive external assembly, or `Framework`);
- containing assembly identity.

Only accessible named types are indexed. Current-project `public` and `internal`
types are eligible. Types from other projects/assemblies must be public. Referenced
projects are compiled as separate Roslyn assemblies rather than merged with current
source, preserving accessibility boundaries.

## Deterministic priority

For a Tiny document, candidates are ordered by:

1. same namespace;
2. namespace listed by an explicit `u:`;
3. Tiny.CSharp source;
4. handwritten C# source;
5. direct project reference;
6. transitive project reference;
7. direct external/package assembly;
8. transitive external/package assembly;
9. framework source;
10. namespace ordinal order;
11. qualified-name ordinal order;
12. assembly identity ordinal order.

Generic arity must match. For example, `List<s>` resolves only against named types
called `List` with arity 1.

## TCS2001: ambiguity

Same-namespace and explicitly imported namespaces first narrow the effective binding
set. If a single `u:` namespace identifies exactly one candidate, the ambiguity is
resolved and no warning is emitted.

If the effective binding set still contains more than one candidate, warning
`TCS2001` lists candidates and the selected type. The generated property is fully
qualified to ensure that the deterministic selection remains valid C# even when
conflicting imports exist.

## TCS2002: unresolved name

No candidate produces a warning, not an error. Tiny.CSharp leaves the name unchanged
in generated C#, preserving Roslyn as the final authority.

## Automatic imports

A unique selected type in another namespace adds a deterministic using directive.
When `EmitAutomaticUsings` is disabled, the selected type is fully qualified
instead.

## Reference inputs

The resolver currently consumes:

- all valid Tiny.CSharp project declarations as stubs;
- handwritten C# from the current project;
- recursively declared `ProjectReference` projects;
- package compile assets from restored `obj/project.assets.json`;
- explicit `Reference/HintPath` assemblies;
- trusted platform assemblies.

It does not scan arbitrary DLL directories.

## Current boundary

`project.assets.json` is already evaluated by restore, but project-reference and
direct-package classification still starts from static project XML. The next layer
is full MSBuild evaluation for conditional items, properties, target-framework
selection, central package management edge cases, and other build-graph semantics.
