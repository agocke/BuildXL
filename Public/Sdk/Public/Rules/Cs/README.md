# Sdk.Rules.Cs — Bazel-style C# rules (prototype)

A small **rules layer** on top of `Sdk.Managed`, modeled after
`@rules_dotnet//dotnet:defs.bzl` (`csharp_binary` / `csharp_library`).

Built on top of the generic `Sdk.Rules` foundation which provides
language-agnostic Bazel primitives (`Provider`, `DefaultInfo`, `depset`,
`select`). See `../Core/README.md` for the full concept mapping.

The point of this prototype is **not** to add a new compiler integration —
`Sdk.Managed` already wraps `csc` and the .NET runtime story. The point is to
demonstrate a Bazel-shaped *rule discipline* on top of DScript:

1. **Rules return providers, not files.** Every rule returns a typed `CsInfo`
   record (which extends `DefaultInfo` from `Sdk.Rules`). Callers read `.name`,
   `.targetFramework`, `.files`, etc. They never see — and never construct —
   output paths.
2. **Dependencies are providers.** The `deps` attribute of a rule takes
   `CsInfo[]` (returned from other rules), not `File[]` or `Reference[]`. The
   transitive reference closure is built automatically inside the rule.
3. **The rule SDK is the only layer allowed to call the engine API.** Nothing
   above `csharp.dsc` calls `Transformer.execute` or constructs paths. BUILD
   files are pure rule-call sites.
4. **Generic tools work via DefaultInfo.** Any packager, deployer, or test
   runner can consume `.files` and `.runfiles` without knowing the source
   language — just like Bazel's `DefaultInfo`.

This mirrors the layering Bazel enforces between `*.bzl` (rule code) and
`BUILD.bazel` (rule-call sites). BuildXL has the matching enforcement
mechanism out of the box — see the `NoTransformers` policy rule
(`Public/Src/FrontEnd/Script/RuntimeModel/AstBridge/Linter/PolicyRules/EnforceNoTransformersRule.cs`).

## API

```typescript
interface CsInfo {
    name: string;
    targetFramework: string;
    assembly: Shared.Assembly;   // opaque to BUILD-file authors
    deps: CsInfo[];
}

interface CsBinaryInfo extends CsInfo {
    executable: File;
    runtimeConfigFiles: File[];
}

function csharpLibrary(args: CsLibraryAttrs): CsInfo;
function csharpBinary (args: CsBinaryAttrs ): CsBinaryInfo;
function transitiveClosure(info: CsInfo): CsInfo[];   // depset-equivalent
```

Common attributes: `name`, `srcs`, `deps`, `defines`, `allowUnsafe`,
`nullable`, `framework`. `csharpBinary` additionally accepts `deploymentStyle`
(`"frameworkDependent"` — the default — or `"selfContained"`).

## Usage

```typescript
// Examples/RulesCs/Hello/BUILD.dsc
import {csharpLibrary, csharpBinary} from "Sdk.Rules.Cs";

const greet = csharpLibrary({
    name: "Greeter",
    srcs: [f`src/Greeter.cs`],
});

@@public
export const hello = csharpBinary({
    name: "hello",
    srcs: [f`src/Program.cs`],
    deps: [greet],
});
```

That's the whole BUILD file. Notice:
- No `Transformer.execute`, no `importFrom("Sdk.Managed")`, no `Csc.compile`,
  no path construction.
- `deps: [greet]` passes a *provider*, not a file.

## What's deliberately **out** of scope for the prototype

These would be the next pieces in a real port — none of them require engine
changes, only more rule code:

- `csharp_test` (one more rule wrapping `Managed.test`).
- Resources / `data` attribute (runfiles layout).
- Per-target visibility finer than module-level (today: use module
  `allowedDependencies` / `allowedDependents` in `module.config.bm`).
- `select()` over named configuration flags (today: branch on `qualifier`).
- Transitions (would wrap `withQualifier`).
- Aspect-equivalent graph traversals beyond `transitiveClosure`.
- Strict-deps enforcement (only direct `deps` are visible at compile time —
  this prototype passes through whatever `Managed.library` allows).
