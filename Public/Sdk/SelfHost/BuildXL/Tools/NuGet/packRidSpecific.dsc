// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import {NugetPacker}                from "BuildXL.Tools";

import * as Deployment              from "Sdk.Deployment";
import * as Managed                 from "Sdk.Managed.Shared";
import * as Xml                     from "Sdk.Xml";

const ridPackTool: Transformer.ToolDefinition = getNugetPackerTool();

function getNugetPackerTool(): Transformer.ToolDefinition {
    switch (Context.getCurrentHost().os) {
        case "win":
            return NugetPacker.withQualifier({targetFramework: "net9.0", targetRuntime: "win-x64", configuration: "release"}).tool;
        case "macOS":
            return NugetPacker.withQualifier({targetFramework: "net9.0", targetRuntime: "osx-x64", configuration: "release"}).tool;
        case "unix":
            return NugetPacker.withQualifier({targetFramework: "net9.0", targetRuntime: "linux-x64", configuration: "release"}).tool;
        default:
            return Contract.fail(`NugetPacker is not supported on current host OS '${Context.getCurrentHost().os}'`);
    }
}

/**
 * Configuration for a single command exposed by the dotnet tool.
 */
@@public
export interface ToolCommand {
    /** The command name users will type (e.g. "bxl"). */
    name: string;

    /** 
     * The entry point file name.
     * For Runner="dotnet" this is the .dll name (e.g. "bxl.dll").
     * For Runner="executable" this is the native host name (e.g. "bxl" on Linux, "bxl.exe" on Windows).
     */
    entryPoint: string;

    /** How the tool is launched. "dotnet" for managed entry points, "executable" for native apphosts. */
    runner: "dotnet" | "executable";
}

/**
 * Arguments for creating a RID-specific dotnet tool NuGet package.
 */
@@public
export interface PackToolRidPackageArgs {
    /** NuGet package id (e.g. "BuildXL.Tool.win-x64"). */
    id: string;

    /** NuGet package version. */
    version: string;

    /** The deployment to include in the tool package. */
    deployment: Deployment.Definition;

    /** Target framework moniker (e.g. "net9.0"). */
    targetFramework: string;

    /** Runtime identifier (e.g. "win-x64", "linux-x64"). */
    rid: string;

    /** Commands exposed by this tool. */
    commands: ToolCommand[];

    /** Package authors. */
    authors: string;

    /** Package description. */
    description?: string;

    /** Package owners. */
    owners?: string;

    /** Package copyright. */
    copyright?: string;

    /** Package tags. */
    tags?: string;

    /** Options for deployment flattening. */
    deploymentOptions?: Managed.Deployment.FlattenOptions;

    /** File names to exclude from the package. */
    filterFiles?: PathAtom[];
}

/**
 * A reference to a RID-specific package from the pointer package.
 */
@@public
export interface RidPackageReference {
    /** Runtime identifier (e.g. "win-x64"). */
    rid: string;

    /** NuGet package id of the RID-specific package. */
    id: string;
}

/**
 * Arguments for creating the top-level pointer package for a dotnet tool.
 */
@@public
export interface PackToolPointerPackageArgs {
    /** NuGet package id (e.g. "BuildXL.Tool"). */
    id: string;

    /** NuGet package version. */
    version: string;

    /** Target framework moniker (e.g. "net9.0"). */
    targetFramework: string;

    /** Commands exposed by this tool (only name is used; no EntryPoint/Runner in pointer). */
    commands: ToolCommand[];

    /** References to RID-specific packages. */
    ridPackages: RidPackageReference[];

    /** Package authors. */
    authors: string;

    /** Package description. */
    description?: string;

    /** Package owners. */
    owners?: string;

    /** Package copyright. */
    copyright?: string;

    /** Package tags. */
    tags?: string;
}

@@public
export interface ToolPackResult {
    nuPkg: File;
}

/**
 * Creates a RID-specific dotnet tool NuGet package.
 * 
 * Layout:
 *   tools/<tfm>/<rid>/
 *     DotnetToolSettings.xml
 *     <all deployment files>
 * 
 * Nuspec package type: DotnetToolRidPackage
 */
@@public
export function packToolRidPackage(args: PackToolRidPackageArgs): ToolPackResult {
    const outDir = Context.getNewOutputDirectory("dotnet-tool-rid-pack");
    const packName = `${args.id}.${args.version}`;
    const nuspecPath = p`${outDir}/${packName + ".nuspec"}`;
    const nupkgPath = p`${outDir}/${packName + ".nupkg"}`;

    // Generate DotnetToolSettings.xml for the RID package (Version="2")
    const toolSettingsPath = p`${outDir}/DotnetToolSettings.xml`;
    const toolSettings = createRidToolSettings(args.commands);
    const toolSettingsFile = Xml.write(toolSettingsPath, toolSettings);

    // Wrap the deployment under tools/<tfm>/<rid>/ and include the settings file
    const toolSubfolder = r`tools/${args.targetFramework}/${args.rid}`;
    const wrappedDeployment: Deployment.Definition = {
        contents: [
            <Deployment.NestedDefinition>{
                subfolder: toolSubfolder,
                contents: [
                    args.deployment,
                    toolSettingsFile
                ]
            }
        ]
    };

    const nuspecData = createToolNuSpecFile(
        {
            id: args.id,
            version: args.version,
            authors: args.authors,
            description: args.description,
            owners: args.owners,
            copyright: args.copyright,
            tags: args.tags,
            packageType: "DotnetToolRidPackage",
        },
        wrappedDeployment,
        nuspecPath,
        args.deploymentOptions,
        args.filterFiles
    );

    const arguments: Argument[] = [
        Cmd.option("/NuSpecPath:", Artifact.input(nuspecData.nuspec)),
        Cmd.argument("/NoDefaultExcludes"),
        Cmd.option("/Verbosity:", "detailed"),
        Cmd.option("/OutputDirectory:", Artifact.none(outDir)),
        Cmd.flag("/NoPackageAnalysis", true),
    ];

    const execArgs = <Transformer.ExecuteArguments>{
        tool: ridPackTool,
        tags: ["pack", "dotnet-tool"],
        arguments: arguments,
        workingDirectory: outDir,
        allowUndeclaredSourceReads: Context.getCurrentHost().os !== "win",
        dependencies: nuspecData.dependencies,
        outputs: [nupkgPath],
        unsafe: {
            untrackedScopes: [
                ...addIfLazy(Context.getCurrentHost().os === "unix", () => [
                    d`${Context.getMount("UserProfile").path}/.local/share/NuGet`,
                    d`${Context.getMount("UserProfile").path}/.config/share/NuGet`,
                    d`${Context.getMount("UserProfile").path}/.nuget/share/NuGet`,
                ]),
            ],
            untrackedPaths: [
                ...addIfLazy(Context.getCurrentHost().os === "unix", () => [
                    d`${Context.getMount("UserProfile").path}`,
                ]),
            ]
        }
    };

    const executeResult = Transformer.execute(execArgs);
    return { nuPkg: executeResult.getOutputFile(nupkgPath) };
}

/**
 * Creates the top-level pointer package for a dotnet tool.
 *
 * Layout:
 *   tools/<tfm>/any/
 *     DotnetToolSettings.xml
 *
 * Nuspec package type: DotnetTool
 */
@@public
export function packToolPointerPackage(args: PackToolPointerPackageArgs): ToolPackResult {
    const outDir = Context.getNewOutputDirectory("dotnet-tool-pointer-pack");
    const packName = `${args.id}.${args.version}`;
    const nuspecPath = p`${outDir}/${packName + ".nuspec"}`;
    const nupkgPath = p`${outDir}/${packName + ".nupkg"}`;

    // Generate DotnetToolSettings.xml for the pointer package (Version="2")
    const toolSettingsPath = p`${outDir}/DotnetToolSettings.xml`;
    const toolSettings = createPointerToolSettings(args.commands, args.ridPackages);
    const toolSettingsFile = Xml.write(toolSettingsPath, toolSettings);

    // Place settings under tools/<tfm>/any/
    const toolSubfolder = r`tools/${args.targetFramework}/any`;
    const wrappedDeployment: Deployment.Definition = {
        contents: [
            <Deployment.NestedDefinition>{
                subfolder: toolSubfolder,
                contents: [toolSettingsFile]
            }
        ]
    };

    const nuspecData = createToolNuSpecFile(
        {
            id: args.id,
            version: args.version,
            authors: args.authors,
            description: args.description,
            owners: args.owners,
            copyright: args.copyright,
            tags: args.tags,
            packageType: "DotnetTool",
        },
        wrappedDeployment,
        nuspecPath,
        undefined,
        undefined
    );

    const arguments: Argument[] = [
        Cmd.option("/NuSpecPath:", Artifact.input(nuspecData.nuspec)),
        Cmd.argument("/NoDefaultExcludes"),
        Cmd.option("/Verbosity:", "detailed"),
        Cmd.option("/OutputDirectory:", Artifact.none(outDir)),
        Cmd.flag("/NoPackageAnalysis", true),
    ];

    const execArgs = <Transformer.ExecuteArguments>{
        tool: ridPackTool,
        tags: ["pack", "dotnet-tool"],
        arguments: arguments,
        workingDirectory: outDir,
        allowUndeclaredSourceReads: Context.getCurrentHost().os !== "win",
        dependencies: nuspecData.dependencies,
        outputs: [nupkgPath],
        unsafe: {
            untrackedScopes: [
                ...addIfLazy(Context.getCurrentHost().os === "unix", () => [
                    d`${Context.getMount("UserProfile").path}/.local/share/NuGet`,
                    d`${Context.getMount("UserProfile").path}/.config/share/NuGet`,
                    d`${Context.getMount("UserProfile").path}/.nuget/share/NuGet`,
                ]),
            ],
            untrackedPaths: [
                ...addIfLazy(Context.getCurrentHost().os === "unix", () => [
                    d`${Context.getMount("UserProfile").path}`,
                ]),
            ]
        }
    };

    const executeResult = Transformer.execute(execArgs);
    return { nuPkg: executeResult.getOutputFile(nupkgPath) };
}

// ============================================================
// Internal helpers
// ============================================================

interface ToolNuSpecMetadata {
    id: string;
    version: string;
    authors: string;
    description?: string;
    owners?: string;
    copyright?: string;
    tags?: string;
    packageType: "DotnetTool" | "DotnetToolRidPackage";
}

/**
 * Creates DotnetToolSettings.xml for a RID-specific package.
 *
 * ```xml
 * <DotNetCliTool Version="2">
 *   <Commands>
 *     <Command Name="bxl" EntryPoint="bxl" Runner="executable" />
 *   </Commands>
 * </DotNetCliTool>
 * ```
 */
function createRidToolSettings(commands: ToolCommand[]): Xml.Document {
    return Xml.doc(
        Xml.elem("DotNetCliTool",
            Xml.attr("Version", "2"),
            Xml.elem("Commands",
                ...commands.map(cmd =>
                    Xml.elem("Command",
                        Xml.attr("Name", cmd.name),
                        Xml.attr("EntryPoint", cmd.entryPoint),
                        Xml.attr("Runner", cmd.runner)
                    )
                )
            )
        )
    );
}

/**
 * Creates DotnetToolSettings.xml for the top-level pointer package.
 *
 * ```xml
 * <DotNetCliTool Version="2">
 *   <Commands>
 *     <Command Name="bxl" />
 *   </Commands>
 *   <RuntimeIdentifierPackages>
 *     <RuntimeIdentifierPackage RuntimeIdentifier="win-x64" Id="BuildXL.Tool.win-x64" />
 *     ...
 *   </RuntimeIdentifierPackages>
 * </DotNetCliTool>
 * ```
 */
function createPointerToolSettings(commands: ToolCommand[], ridPackages: RidPackageReference[]): Xml.Document {
    return Xml.doc(
        Xml.elem("DotNetCliTool",
            Xml.attr("Version", "2"),
            Xml.elem("Commands",
                ...commands.map(cmd =>
                    Xml.elem("Command",
                        Xml.attr("Name", cmd.name)
                    )
                )
            ),
            Xml.elem("RuntimeIdentifierPackages",
                ...ridPackages.map(rp =>
                    Xml.elem("RuntimeIdentifierPackage",
                        Xml.attr("RuntimeIdentifier", rp.rid),
                        Xml.attr("Id", rp.id)
                    )
                )
            )
        )
    );
}

/**
 * Generates a nuspec file with dotnet tool package type and the deployment files.
 */
function createToolNuSpecFile(
    metadata: ToolNuSpecMetadata,
    deployment: Deployment.Definition,
    nuSpecOutput: Path,
    deploymentOptions: Managed.Deployment.FlattenOptions,
    filterFiles?: PathAtom[]
): { nuspec: File, dependencies: (File | OpaqueDirectory)[] } {

    let optionalElement = (element: string, value: string) => String.isUndefinedOrEmpty(value)
        ? undefined
        : Xml.elem(element, value);

    let dependencies: (File | OpaqueDirectory)[] = [];
    let fileElements: Xml.Element[] = [];

    const flattened = Deployment.flatten(deployment, undefined, deploymentOptions);

    for (let flattenedFile of flattened.flattenedFiles.toArray()) {
        const target = flattenedFile[0];
        let source = flattenedFile[1].file;

        if (filterFiles && filterFiles.indexOf(source.name) !== -1) {
            continue;
        }

        if (target.name !== source.path.name) {
            source = Transformer.copyFile(source, p`${Context.getNewOutputDirectory("tmp_rename_for_nuget")}/${target.name}`);
        }

        dependencies = dependencies.push(source);
        fileElements = fileElements.push(
            Xml.elem("file",
                Xml.attr("src", source),
                Xml.attr("target", target)
            )
        );
    }

    for (let opaque of flattened.flattenedOpaques.toArray()) {
        dependencies = dependencies.push(opaque[1].opaque);
        fileElements = fileElements.push(
            Xml.elem("file",
                Xml.attr("src", [p`${opaque[1].opaque.path}\${opaque[1].subDirectory || r`.`}`, "\\**"]),
                Xml.attr("target", opaque[0])
            )
        );
    }

    const nuSpecDoc = Xml.doc(
        Xml.elem({ local: "package", namespace: "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd" },
            Xml.elem("metadata",
                optionalElement("id", metadata.id),
                optionalElement("version", metadata.version),
                optionalElement("authors", metadata.authors),
                optionalElement("owners", metadata.owners),
                optionalElement("description", metadata.description || `${metadata.id} dotnet tool package`),
                optionalElement("copyright", metadata.copyright),
                optionalElement("tags", metadata.tags),
                Xml.elem("packageTypes",
                    Xml.elem("packageType",
                        Xml.attr("name", metadata.packageType)
                    )
                )
            ),
            Xml.elem("files", ...fileElements)
        )
    );

    const nuspec = Xml.write(nuSpecOutput, nuSpecDoc);

    return {
        nuspec: nuspec,
        dependencies: dependencies,
    };
}
