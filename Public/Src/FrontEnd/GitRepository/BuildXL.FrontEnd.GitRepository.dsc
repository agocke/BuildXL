// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

namespace GitRepository {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.FrontEnd.GitRepository",
        generateLogs: true,
        sources: globR(d`.`, "*.cs"),
        addNotNullAttributeFile: true,
        references: [
            Core.dll,
            Script.dll,
            Sdk.dll,
            TypeScript.Net.dll,
            Utilities.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            ...BuildXLSdk.tplPackages,
        ],
        internalsVisibleTo: [
            "Test.BuildXL.FrontEnd.GitRepository",
        ],
    });
}
