# Sdk.Rules — Generic Bazel-Style Primitives for DScript

A language-agnostic foundation layer that captures core Bazel concepts in
DScript, making it easier to port Bazel builds and write new language rules.

Language-specific rule SDKs (e.g. `Sdk.Rules.Cs`) build on top of this module.

## Bazel → DScript Concept Mapping

| Bazel concept | DScript equivalent | Defined in |
|---|---|---|
| `provider()` | `interface Foo extends Provider {}` | `providers.dsc` |
| `DefaultInfo` | `DefaultInfo` | `providers.dsc` |
| `OutputGroupInfo` | `OutputGroupInfo` | `providers.dsc` |
| `depset()` | `depset(root, getDeps, getName)` | `depset.dsc` |
| `select({...})` | `select(key, branches, default)` | `select.dsc` |
| `Label` | `Label` (type alias for string) | `providers.dsc` |
| `visibility = ["//visibility:public"]` | `@@public export` | (language feature) |
| `visibility = ["//visibility:private"]` | bare `const` (no export) | (language feature) |
| package-default visibility | `export` (no `@@public`) | (language feature) |
| `BUILD.bazel` | `BUILD.dsc` | (naming convention) |
| `load("@rules_foo//...", ...)` | `import {...} from "Sdk.Rules.Foo"` | (language feature) |
| `--platforms=...` / `config_setting` | `qualifier` declaration | (language feature) |

## Providers

Every rule returns a **provider** — a typed record extending `Provider`. The
`kind` field acts as a discriminator so generic tools can identify providers
without importing language-specific SDKs.

`DefaultInfo` is the universal provider: it carries `files` (default outputs)
and `runfiles` (runtime dependencies). Any packaging or deployment tool can
consume `DefaultInfo` regardless of the source language.

```typescript
import {DefaultInfo, Provider} from "Sdk.Rules";

// Language-specific provider extends DefaultInfo:
interface MyLangInfo extends DefaultInfo {
    kind: "MyLangInfo";
    // ... language-specific fields ...
}
```

## depset (Transitive Closure)

The `depset` function walks a provider graph and returns a deduplicated,
dependency-ordered list — the DScript equivalent of Bazel's `depset`:

```typescript
import {depset} from "Sdk.Rules";

const allDeps = depset(myTarget, t => t.deps, t => t.name);
```

## select (Configuration Branching)

The `select` helper dispatches on a qualifier field value — analogous to
Bazel's `select()` over `config_setting`:

```typescript
import {select} from "Sdk.Rules";

const platformLibs = select(qualifier.targetRuntime, new Map([
    ["win-x64",   [winLib]],
    ["linux-x64", [linuxLib]],
]), []);
```

## Visibility Convention

DScript doesn't have Bazel's `visibility` attribute, but its export system
maps cleanly:

| DScript | Bazel equivalent | Meaning |
|---|---|---|
| `@@public export const x = ...` | `visibility = ["//visibility:public"]` | Visible to all modules |
| `export const x = ...` | (default package visibility) | Visible within the module |
| `const x = ...` | `visibility = ["//visibility:private"]` | File-private |

For finer-grained control, use `allowedDependencies` / `allowedDependents` in
`module.config.dsc` — the analogue of Bazel's `package_group`.

## BUILD.dsc Convention

Rule-consumer specs should be named `BUILD.dsc` — directly analogous to
`BUILD.bazel`. Each directory with a `BUILD.dsc` represents a "package" in
Bazel terms. The module's `module.config.dsc` discovers them:

```typescript
module({
    name: "MyProject",
    projects: globR(d`.`, "BUILD.dsc"),
});
```
