// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// =============================================================================
// Sdk.Rules — Generic depset (transitive closure) helper.
//
// Bazel's `depset` is a lazily-evaluated set that accumulates transitive
// dependencies. DScript doesn't have lazy evaluation, but we can provide the
// same *interface* — a function that walks a provider graph and returns a
// deduplicated list in dependency order.
//
// This is language-agnostic: it works on any type T that has a string identity
// and children, via caller-supplied accessor functions.
//
// Bazel equivalent:
//   depset(direct = [...], transitive = [dep.files for dep in ctx.attr.deps])
// =============================================================================

import {Provider} from "Sdk.Rules";

/**
 * Walks a provider graph rooted at `root`, collecting all reachable nodes in
 * dependency-first (postorder) traversal. Duplicates are eliminated by the
 * identity returned from `getName`.
 *
 * @param root     - The starting provider node.
 * @param getDeps  - Returns direct dependencies of a node.
 * @param getName  - Returns a unique identity string for deduplication.
 * @returns A deduplicated array of all transitive providers in dependency order.
 *
 * Usage example (from a C# rules layer):
 *   const closure = depset(myLib, info => info.deps, info => info.name);
 */
@@public
export function depset<T>(root: T, getDeps: (node: T) => T[], getName: (node: T) => string): T[] {
    return depsetVisit(root, getDeps, getName, { seen: Set.empty<string>(), out: [] }).out;
}

/**
 * Collects files from a provider graph by walking deps and extracting files
 * from each node. Useful for building a runfiles set from transitive deps.
 *
 * @param root      - The starting provider node.
 * @param getDeps   - Returns direct dependencies of a node.
 * @param getName   - Returns a unique identity string for deduplication.
 * @param getFiles  - Extracts files from a single node.
 * @returns All files from all transitive providers, deduplicated by provider.
 */
@@public
export function depsetFiles<T>(
    root: T,
    getDeps: (node: T) => T[],
    getName: (node: T) => string,
    getFiles: (node: T) => File[]
): File[] {
    const nodes = depset(root, getDeps, getName);
    // Flatten all files from all nodes. File deduplication relies on BuildXL's
    // content-addressed storage — duplicates in the array are harmless but we
    // keep the list clean by trusting provider-level dedup.
    let result: File[] = [];
    for (const n of nodes) {
        result = result.concat(getFiles(n));
    }
    return result;
}

// -- Internal helpers ---------------------------------------------------------

interface DepsetState<T> {
    seen: Set<string>;
    out: T[];
}

function depsetVisit<T>(
    node: T,
    getDeps: (n: T) => T[],
    getName: (n: T) => string,
    state: DepsetState<T>
): DepsetState<T> {
    const id = getName(node);
    if (state.seen.contains(id)) {
        return state;
    }
    let next: DepsetState<T> = { seen: state.seen.add(id), out: state.out };
    for (const d of getDeps(node)) {
        next = depsetVisit(d, getDeps, getName, next);
    }
    return { seen: next.seen, out: next.out.push(node) };
}
