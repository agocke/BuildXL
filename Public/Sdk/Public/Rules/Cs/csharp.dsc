// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// =============================================================================
// Sdk.Rules.Cs - A Bazel-style rules layer for C#, prototyped on top of Sdk.Managed.
//
// Goals:
//  - Provider pattern: rules return typed records (CsInfo / CsBinaryInfo); they
//    do NOT return raw File/Directory and do NOT let callers pick output paths.
//  - Deps-as-providers: a rule depends on the *provider* of another rule, not
//    on bare files. The transitive reference closure is built automatically.
//  - Layering: this SDK is allowed to reach into Sdk.Managed (the analogue of
//    a *.bzl file calling ctx.actions); BUILD-style specs only call rule
//    functions defined here.
//
// This is intentionally a thin prototype - it supports the most common shape
// of `csharp_binary` / `csharp_library` from rules_dotnet:
//   - sources, deps, defines, allowUnsafe, nullable, framework override.
// Things like resources, runfiles layout, codegen toolchains, multi-targeting
// per-rule, and per-target visibility are out of scope for the prototype.
// =============================================================================

import * as Managed    from "Sdk.Managed";
import * as Shared     from "Sdk.Managed.Shared";
import * as Frameworks from "Sdk.Managed.Frameworks";

export declare const qualifier : Shared.TargetFrameworks.All;

// -----------------------------------------------------------------------------
// Providers
// -----------------------------------------------------------------------------

/**
 * Provider returned by every C# rule. Rules consume each other by reading
 * fields off this record - never by reading raw output paths.
 *
 * Keeping the underlying Managed.Assembly inside the provider lets a downstream
 * rule (e.g. csharpBinary) feed it back in as a reference without the BUILD
 * file ever touching a File or PathAtom.
 */
@@public
export interface CsInfo {
    /** Logical name of the target (matches the assembly name). */
    name: string;

    /** Target framework this assembly was compiled for. */
    targetFramework: string;

    /** The compiled assembly, as understood by Sdk.Managed. */
    assembly: Shared.Assembly;

    /** Direct dependencies of this target (the providers that were passed in). */
    deps: CsInfo[];
}

/**
 * Provider returned by csharpBinary. Extends CsInfo with an executable handle
 * and the runtime assembly closure, so a runner / packager can find what to
 * actually launch without inspecting paths.
 */
@@public
export interface CsBinaryInfo extends CsInfo {
    /** The primary executable file (managed exe). */
    executable: File;

    /** The .runtimeconfig.json / app.config files, if any. */
    runtimeConfigFiles: File[];
}

// -----------------------------------------------------------------------------
// Common rule attributes
// -----------------------------------------------------------------------------

/** Attributes shared by csharpLibrary and csharpBinary - the Bazel-style "attrs". */
@@public
export interface CsCommonAttrs {
    /** Logical target name. Becomes the assembly name. */
    name: string;

    /** Source files. */
    srcs: File[];

    /** Dependencies, expressed as providers from other csharp* rules. */
    deps?: CsInfo[];

    /** Preprocessor symbols. */
    defines?: string[];

    /** Allow `unsafe` blocks. Defaults to false. */
    allowUnsafe?: boolean;

    /** C# nullable annotation context. */
    nullable?: boolean;

    /**
     * Override the target framework. By default the framework is chosen from
     * the qualifier (the Bazel-equivalent of the build configuration).
     */
    framework?: Shared.Framework;
}

@@public
export interface CsLibraryAttrs extends CsCommonAttrs {
}

@@public
export interface CsBinaryAttrs extends CsCommonAttrs {
    /**
     * Deployment style. Defaults to "frameworkDependent" - matches what
     * `csharp_binary` produces by default in rules_dotnet.
     */
    deploymentStyle?: Shared.ApplicationDeploymentStyle;
}

// -----------------------------------------------------------------------------
// Rules
// -----------------------------------------------------------------------------

/**
 * csharpLibrary - the analogue of rules_dotnet's `csharp_library`.
 *
 * Compiles the given sources into a managed library and returns a CsInfo
 * provider. The caller never sees output paths.
 */
@@public
export function csharpLibrary(args: CsLibraryAttrs): CsInfo {
    const result = Managed.library(toManagedArgs(args, /*isExe*/ false));
    return makeProvider(args, result);
}

/**
 * csharpBinary - the analogue of rules_dotnet's `csharp_binary`.
 *
 * Compiles the given sources into a managed executable, deploys the runtime
 * config, and returns a CsBinaryInfo provider whose `executable` field points
 * at the runnable .exe / .dll.
 */
@@public
export function csharpBinary(args: CsBinaryAttrs): CsBinaryInfo {
    const managedArgs = toManagedArgs(args, /*isExe*/ true).merge<Managed.Arguments>({
        deployRuntimeConfigFile: true,
        deploymentStyle: args.deploymentStyle || "frameworkDependent",
    });

    const result = Managed.executable(managedArgs);
    const base = makeProvider(args, result);

    return base.merge<CsBinaryInfo>({
        executable: Shared.getExecutable(result),
        runtimeConfigFiles: result.runtimeConfigFiles || [],
    });
}

// -----------------------------------------------------------------------------
// Internals
// -----------------------------------------------------------------------------

/**
 * Lower a rule's attributes into the shape Sdk.Managed expects.
 *
 * This is the only place in the SDK where the wrapped engine API is touched;
 * everything above this line is pure provider plumbing.
 */
function toManagedArgs(args: CsCommonAttrs, isExe: boolean): Managed.Arguments {
    const framework = args.framework || Frameworks.framework;

    // Bazel deps are providers; lower them to Managed.Reference[] for csc.
    const references: Shared.Reference[] = (args.deps || []).map(d => d.assembly);

    return <Managed.Arguments>{
        assemblyName:      args.name,
        sources:           args.srcs,
        framework:         framework,
        references:        references,
        defineConstants:   args.defines,
        allowUnsafeBlocks: args.allowUnsafe,
        nullable:          args.nullable,
    };
}

/** Wrap a Managed.Result into the CsInfo provider record. */
function makeProvider(args: CsCommonAttrs, result: Managed.Result): CsInfo {
    return {
        name:            args.name,
        targetFramework: result.targetFramework,
        assembly:        result,
        deps:            args.deps || [],
    };
}

// -----------------------------------------------------------------------------
// Provider helpers (the depset-equivalent for callers that want to walk deps).
// -----------------------------------------------------------------------------

/**
 * Walks a CsInfo provider and its transitive `deps`, returning a deduplicated
 * list of providers in dependency order. This is the small "depset" stand-in
 * mentioned in the design notes - good enough for a prototype.
 */
@@public
export function transitiveClosure(info: CsInfo): CsInfo[] {
    return visit(info, { seen: Set.empty<string>(), out: [] }).out;
}

interface ClosureState {
    seen: Set<string>;
    out: CsInfo[];
}

function visit(node: CsInfo, state: ClosureState): ClosureState {
    if (state.seen.contains(node.name)) {
        return state;
    }
    let next: ClosureState = { seen: state.seen.add(node.name), out: state.out };
    for (const d of node.deps) {
        next = visit(d, next);
    }
    return { seen: next.seen, out: next.out.push(node) };
}
