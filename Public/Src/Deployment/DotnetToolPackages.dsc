// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Branding from "BuildXL.Branding";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";
import * as Managed from "Sdk.Managed.Shared";
import * as RidPack from "Sdk.BuildXL.Tools.NuGet";

namespace DotnetToolPackages {
    export declare const qualifier: { configuration: "debug" | "release" };

    const defaultTargetFramework = Managed.TargetFrameworks.DefaultTargetFramework;

    // win-x64 native binaries (DetoursServices, etc.) require MSVC, so the win-x64
    // RID package can only be built on Windows. The pointer (meta) package always
    // references win-x64 so the published win-x64 nupkg (built on a separate Windows
    // CI runner) is wired up correctly.
    const canBuildAllPackagesOnThisHost = Context.getCurrentHost().os === "win";

    const packageNamePrefix =
        BuildXLSdk.Flags.isExperimentalDeployment
            ? "agtest.bxl"
            : BuildXLSdk.Flags.isMicrosoftInternal
                ? "BuildXL"
                : "Microsoft.BuildXL";

    const toolPackageId = BuildXLSdk.Flags.isExperimentalDeployment
        ? `${packageNamePrefix}.tool`
        : `${packageNamePrefix}.Tool`;

    const reducedDeploymentOptions: Managed.Deployment.FlattenOptions = {
        skipPdb: false,
        skipXml: true,
    };

    const toolCommand: RidPack.ToolCommand = {
        name: "bxl",
        entryPoint: "bxl.dll",
        runner: "dotnet",
    };

    // App deployments for each RID
    // win-x64 deployment can only be built on Windows (MSVC native tools required)
    const winX64Deployment = !canBuildAllPackagesOnThisHost ? undefined : importFrom("BuildXL.App").withQualifier({
        targetFramework: defaultTargetFramework,
        targetRuntime: "win-x64"
    }).deployment;

    const osxX64Deployment = importFrom("BuildXL.App").withQualifier({
        targetFramework: defaultTargetFramework,
        targetRuntime: "osx-x64"
    }).deployment;

    const linuxX64Deployment = importFrom("BuildXL.App").withQualifier({
        targetFramework: defaultTargetFramework,
        targetRuntime: "linux-x64"
    }).deployment;

    function makeToolDescription(rid: string): string {
        return `BuildXL dotnet tool (${rid}). ${Branding.shortProductName} is a build engine for large-scale distributed, cached, and incremental builds.`;
    }

    // RID-specific tool packages
    const winX64Tool = !canBuildAllPackagesOnThisHost ? undefined : RidPack.packToolRidPackage({
        id: `${toolPackageId}.win-x64`,
        version: Branding.Nuget.packageVersion,
        deployment: winX64Deployment,
        targetFramework: defaultTargetFramework,
        rid: "win-x64",
        commands: [toolCommand],
        authors: Branding.Nuget.packageAuthors,
        owners: Branding.Nuget.packageOwners,
        copyright: Branding.Nuget.packageCopyright,
        description: makeToolDescription("win-x64"),
        tags: `${Branding.company} ${Branding.shortProductName} Build Tool`,
        deploymentOptions: reducedDeploymentOptions,
        filterFiles: [a`DetoursServices.pdb`, a`BuildXLAria.pdb`, a`BuildXLNatives.pdb`],
    });

    const osxX64Tool = RidPack.packToolRidPackage({
        id: `${toolPackageId}.osx-x64`,
        version: Branding.Nuget.packageVersion,
        deployment: osxX64Deployment,
        targetFramework: defaultTargetFramework,
        rid: "osx-x64",
        commands: [toolCommand],
        authors: Branding.Nuget.packageAuthors,
        owners: Branding.Nuget.packageOwners,
        copyright: Branding.Nuget.packageCopyright,
        description: makeToolDescription("osx-x64"),
        tags: `${Branding.company} ${Branding.shortProductName} Build Tool`,
        deploymentOptions: reducedDeploymentOptions,
    });

    const linuxX64Tool = RidPack.packToolRidPackage({
        id: `${toolPackageId}.linux-x64`,
        version: Branding.Nuget.packageVersion,
        deployment: linuxX64Deployment,
        targetFramework: defaultTargetFramework,
        rid: "linux-x64",
        commands: [toolCommand],
        authors: Branding.Nuget.packageAuthors,
        owners: Branding.Nuget.packageOwners,
        copyright: Branding.Nuget.packageCopyright,
        description: makeToolDescription("linux-x64"),
        tags: `${Branding.company} ${Branding.shortProductName} Build Tool`,
        deploymentOptions: reducedDeploymentOptions,
    });

    // Top-level pointer package
    const pointerPackage = RidPack.packToolPointerPackage({
        id: toolPackageId,
        version: Branding.Nuget.packageVersion,
        targetFramework: defaultTargetFramework,
        commands: [toolCommand],
        ridPackages: [
            { rid: "win-x64", id: `${toolPackageId}.win-x64` },
            { rid: "osx-x64", id: `${toolPackageId}.osx-x64` },
            { rid: "linux-x64", id: `${toolPackageId}.linux-x64` },
        ],
        authors: Branding.Nuget.packageAuthors,
        owners: Branding.Nuget.packageOwners,
        copyright: Branding.Nuget.packageCopyright,
        description: `BuildXL dotnet tool. ${Branding.shortProductName} is a build engine for large-scale distributed, cached, and incremental builds. Install with: dotnet tool install -g ${toolPackageId}`,
        tags: `${Branding.company} ${Branding.shortProductName} Build Tool`,
    });

    @@public
    export const deployment: Deployment.Definition = {
        contents: [
            ...addIfLazy(canBuildAllPackagesOnThisHost, () => [
                winX64Tool.nuPkg,
            ]),
            osxX64Tool.nuPkg,
            linuxX64Tool.nuPkg,
            pointerPackage.nuPkg,
        ]
    };
}
