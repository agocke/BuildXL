// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// =============================================================================
// Sdk.Rules — select() helper for configuration-based branching.
//
// Bazel's `select()` chooses a value based on which `config_setting` matches
// the current build configuration. In DScript the equivalent mechanism is the
// qualifier — every spec declares a qualifier type and can branch on its fields.
//
// This helper provides a familiar `select()`-shaped API that dispatches on a
// qualifier field value. It doesn't add new capability (you can always write an
// `if` or ternary on `qualifier.targetFramework`), but it signals intent to
// Bazel porters and keeps BUILD files readable.
//
// Bazel equivalent:
//   select({
//       "@platforms//os:linux": [...],
//       "@platforms//os:windows": [...],
//       "//conditions:default": [...],
//   })
// =============================================================================

/**
 * Selects a value based on a string key (typically a qualifier field value).
 * If the key isn't found in the map, returns `defaultValue`.
 *
 * Usage:
 *   const platformDeps = select(qualifier.targetRuntime, {
 *       "win-x64":   [winOnlyLib],
 *       "linux-x64": [linuxOnlyLib],
 *   }, []);
 *
 * @param key          - The configuration value to match on.
 * @param branches     - Map from config values to results.
 * @param defaultValue - Fallback if no branch matches (Bazel's "//conditions:default").
 */
@@public
export function select<T>(key: string, branches: Map<string, T>, defaultValue: T): T {
    const result = branches.get(key);
    return result !== undefined ? result : defaultValue;
}

/**
 * Multi-select: evaluates multiple keys and merges all matching branches
 * (useful when a target needs deps from several matching conditions).
 *
 * @param keys         - Configuration values to match on.
 * @param branches     - Map from config values to arrays of results.
 * @param defaultValue - Fallback array if no branch matches.
 */
@@public
export function multiSelect<T>(keys: string[], branches: Map<string, T[]>, defaultValue: T[]): T[] {
    let result: T[] = [];
    let matched = false;
    for (const k of keys) {
        const branch = branches.get(k);
        if (branch !== undefined) {
            result = result.concat(branch);
            matched = true;
        }
    }
    return matched ? result : defaultValue;
}
