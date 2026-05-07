// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// =============================================================================
// Sdk.Rules — Generic provider interfaces (Bazel-style).
//
// Every rule in a Bazel-style layer returns a *provider* — a typed record that
// downstream rules consume. This file defines the base provider interface and
// the universal built-in providers that Bazel offers.
//
// Mapping to Bazel concepts:
//   DScript interface extending Provider  ↔  provider() factory in Starlark
//   DefaultInfo                           ↔  DefaultInfo built-in provider
//   OutputGroupInfo                       ↔  OutputGroupInfo built-in provider
//   @@public export                       ↔  visibility = ["//visibility:public"]
//   export (no @@public)                  ↔  package-visible (default)
//   bare const                            ↔  file-private (not visible outside)
// =============================================================================

/**
 * Base interface for all providers. Every rule must return something that
 * extends Provider. The `kind` field is a discriminator string (like Bazel's
 * provider name) that lets generic tools identify what kind of provider they
 * received without importing the language-specific SDK.
 *
 * Bazel equivalent: the object returned by `provider()`.
 */
@@public
export interface Provider {
    /** Discriminator identifying the provider type (e.g. "CsInfo", "CcInfo"). */
    kind: string;
}

/**
 * The universal provider that every rule should return (possibly alongside
 * language-specific providers). Any generic tool — packager, deployer, test
 * runner — can consume DefaultInfo without knowing the source language.
 *
 * Bazel equivalent: `DefaultInfo(files = depset([...]), runfiles = ...)`.
 *
 * Note: Modern Bazel (6.0+) deprecated `data_runfiles` in favor of a single
 * unified `runfiles` field. We follow the modern convention here.
 */
@@public
export interface DefaultInfo extends Provider {
    /** The default output files produced by this target. */
    files: File[];

    /**
     * Files needed at runtime (e.g. shared libraries, config files, data).
     * Corresponds to Bazel's unified `runfiles` field.
     */
    runfiles?: File[];
}

/**
 * Provider for named output groups — lets a target expose multiple sets of
 * files under different names (e.g. "headers", "debug_symbols").
 *
 * Bazel equivalent: `OutputGroupInfo(group_name = depset([...]))`.
 */
@@public
export interface OutputGroupInfo extends Provider {
    /** Named groups of output files. Key = group name, Value = files in group. */
    groups: Map<string, File[]>;
}

/**
 * A Label is a string that identifies a target. In Bazel this is
 * `//package:target`; in DScript it maps to `"ModuleName"` + the exported
 * const name. This type alias signals intent and aids future migration tooling.
 */
@@public
export type Label = string;
