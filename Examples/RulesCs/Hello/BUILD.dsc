// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// =============================================================================
// Example "BUILD" file using the Sdk.Rules.Cs prototype.
//
// This is what a BUILD.bazel that calls `csharp_library` / `csharp_binary`
// would look like, expressed in DScript. The key properties to notice:
//
//   * The only import is the rules SDK. No `Sdk.Managed`, no `Sdk.Transformers`,
//     no `Sdk.Managed.Tools.Csc` - those are implementation details of the rules.
//   * The deps wiring (`deps: [greet]`) passes a *provider*, not files or paths.
//   * No File output paths are constructed by the BUILD author.
// =============================================================================

import {csharpBinary, csharpLibrary} from "Sdk.Rules.Cs";
import * as Shared from "Sdk.Managed.Shared";

// The qualifier maps onto Bazel's build configuration. Selecting it here is
// the analogue of `--platforms=...` on the command line.
export declare const qualifier : Shared.TargetFrameworks.CoreClr;

// csharp_library(name = "greeter", srcs = ["src/Greeter.cs"])
const greeter = csharpLibrary({
    name: "Greeter",
    srcs: [f`src/Greeter.cs`],
    nullable: true,
});

// csharp_binary(name = "hello", srcs = ["src/Program.cs"], deps = [":greeter"])
@@public
export const hello = csharpBinary({
    name: "hello",
    srcs: [f`src/Program.cs`],
    deps: [greeter],
    nullable: true,
});
