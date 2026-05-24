// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using BuildXL.Native.IO;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using System.Collections.Generic;
using System.Linq;

namespace Tool.Download
{
    /// <summary>
    /// Extracts a given file with different formats (zip, tar, etc.)
    /// </summary>
    internal sealed class Extractor : ToolProgram<ExtractorArgs>
    {
        private Extractor() : base("Extractor")
        {
        }

        /// <nodoc />
        public static int Main(string[] arguments)
        {
            return new Extractor().MainHandler(arguments);
        }

        /// <inheritdoc />
        public override bool TryParse(string[] rawArgs, out ExtractorArgs arguments)
        {
            try
            {
                arguments = new ExtractorArgs(rawArgs);
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.GetLogEventMessage());
                arguments = null;
                return false;
            }
        }

        /// <inheritdoc />
        public override int Run(ExtractorArgs arguments)
        {
            return TryExtractToDisk(arguments) ? 0 : 1;
        }

        private bool TryExtractToDisk(ExtractorArgs arguments)
        {
            var archive = arguments.PathToFileToExtract;
            var target = arguments.ExtractDirectory;
            try
            {
                FileUtilities.DeleteDirectoryContents(target, false);
                FileUtilities.CreateDirectory(target);
            }
            catch (BuildXLException e)
            {
                ErrorExtractingArchive(archive, target, e.Message);
                return false;
            }

            switch (arguments.ArchiveType)
            {
                case DownloadArchiveType.Zip:
                    try
                    {
                        // SharpZipLib does not work well on mac and nested files are not properly handled when the zip file is constructed on Windows (with backslashes)
                        System.IO.Compression.ZipFile.ExtractToDirectory(archive, target);
                    }
                    catch (Exception e) when (e is IOException || e is DirectoryNotFoundException || e is PathTooLongException)
                    {
                        ErrorExtractingArchive(archive, target, e.Message);
                        return false;
                    }

                    break;
                case DownloadArchiveType.Gzip:
                    try
                    {
                        var targetFile = Path.Combine(target, Path.GetFileNameWithoutExtension(arguments.PathToFileToExtract));

                        using (var fileStream = File.OpenRead(arguments.PathToFileToExtract))
                        using (var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
                        using (var output = FileUtilities.CreateFileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.Read))
                        {
                            gzipStream.CopyTo(output);
                        }
                    }
                    catch (InvalidDataException e)
                    {
                        ErrorExtractingArchive(archive, target, e.Message);
                        return false;
                    }

                    break;
                case DownloadArchiveType.Tar:
                    try
                    {
                        using (var fileStream = File.OpenRead(arguments.PathToFileToExtract))
                        {
                            ExtractTar(fileStream, target);
                        }
                    }
                    catch (InvalidDataException e)
                    {
                        ErrorExtractingArchive(archive, target, e.Message);
                        return false;
                    }

                    break;
                case DownloadArchiveType.Tgz:
                    try
                    {
                        using (var fileStream = File.OpenRead(arguments.PathToFileToExtract))
                        using (var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
                        {
                            ExtractTar(gzipStream, target);
                        }
                    }
                    catch (InvalidDataException e)
                    {
                        ErrorExtractingArchive(archive, target, e.Message);
                        return false;
                    }

                    break;
                case DownloadArchiveType.File:
                    Console.WriteLine("Specified download archive type is 'File'. Nothing to extract.");
                    return true;
                default:
                    throw Contract.AssertFailure($"Unexpected archive type '{arguments.ArchiveType}'");
            }

            // Need to set the execute permissions bit for all the extracted files.
            SetExecutePermissionsForExtractedFiles(target);
            try
            {
                if (!FileUtilities.DirectoryExistsNoFollow(target))
                {
                    ErrorNothingExtracted(archive, target);
                    return false;
                }
            }
            catch (BuildXLException e)
            {
                ErrorExtractingArchive(archive, target, e.Message);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Extracts a tar stream to the target directory, handling regular files, directories, symbolic links, and hard links.
        /// </summary>
        /// <remarks>
        /// Throws on path-traversal entries (entries that would write outside <paramref name="targetDirectory"/>),
        /// on symlink creation failures, and on unresolvable hard links. Device nodes, FIFOs and other
        /// non-portable entry types are skipped with a diagnostic.
        /// </remarks>
        private static void ExtractTar(Stream tarStream, string targetDirectory)
        {
            using var reader = new TarReader(tarStream);

            var fullTargetDirectory = Path.GetFullPath(targetDirectory);
            var targetWithSeparator = fullTargetDirectory.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? fullTargetDirectory
                : fullTargetDirectory + Path.DirectorySeparatorChar;

            // Hard links whose targets haven't been extracted yet are deferred to a second pass.
            var deferredHardLinks = new List<(string targetPath, string resolvedLinkTarget)>();

            while (reader.GetNextEntry() is TarEntry entry)
            {
                var entryName = entry.Name;
                if (string.IsNullOrEmpty(entryName))
                {
                    continue;
                }

                var targetPath = Path.GetFullPath(Path.Combine(targetDirectory, entryName.Replace('/', Path.DirectorySeparatorChar)));

                // Sanitize: prevent path traversal. After normalization the target must lie
                // strictly under targetDirectory. Done on the resolved path rather than a naive
                // string check on the entry name so legitimate names like "foo..bar" are allowed.
                if (!targetPath.StartsWith(targetWithSeparator, StringComparison.Ordinal) && targetPath != fullTargetDirectory)
                {
                    throw new IOException($"Refusing to extract tar entry '{entryName}': resolves outside target directory '{fullTargetDirectory}'.");
                }

                switch (entry.EntryType)
                {
                    case TarEntryType.Directory:
                        Directory.CreateDirectory(targetPath);
                        break;

                    case TarEntryType.SymbolicLink:
                        Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

                        // Determine whether the symlink target is a file or directory.
                        // Resolve the target relative to the symlink's parent directory.
                        bool isTargetFile = true;
                        var symlinkDir = Path.GetDirectoryName(targetPath);
                        var resolvedSymlinkTarget = Path.GetFullPath(Path.Combine(symlinkDir, entry.LinkName));
                        if (Directory.Exists(resolvedSymlinkTarget))
                        {
                            isTargetFile = false;
                        }

                        var symlinkResult = FileUtilities.TryCreateSymbolicLink(targetPath, entry.LinkName, isTargetFile);
                        if (!symlinkResult.Succeeded)
                        {
                            throw new IOException($"Failed to create symlink '{targetPath}' -> '{entry.LinkName}': {symlinkResult.Failure.Describe()}");
                        }
                        break;

                    case TarEntryType.HardLink:
                        // Hard links reference another entry in the archive by path.
                        // The link target path is relative to the archive root.
                        var linkName = entry.LinkName;
                        var resolvedLinkTarget = Path.GetFullPath(Path.Combine(targetDirectory, linkName.Replace('/', Path.DirectorySeparatorChar)));
                        if (!resolvedLinkTarget.StartsWith(targetWithSeparator, StringComparison.Ordinal) && resolvedLinkTarget != fullTargetDirectory)
                        {
                            throw new IOException($"Refusing to materialise hard link '{entryName}' -> '{linkName}': link target resolves outside target directory '{fullTargetDirectory}'.");
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

                        if (File.Exists(resolvedLinkTarget))
                        {
                            File.Copy(resolvedLinkTarget, targetPath, overwrite: true);
                        }
                        else
                        {
                            // Target not yet extracted; defer to a second pass.
                            deferredHardLinks.Add((targetPath, resolvedLinkTarget));
                        }
                        break;

                    case TarEntryType.RegularFile:
                    case TarEntryType.V7RegularFile:
                        Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                        using (var outputStream = File.Create(targetPath))
                        {
                            entry.DataStream?.CopyTo(outputStream);
                        }
                        break;

                    default:
                        // Device nodes, FIFOs, character/block devices, sparse files, etc. are not
                        // portable across the platforms we support. Surface them so an archive that
                        // unexpectedly relies on them isn't silently truncated (the bug class that
                        // originally dropped npm/npx symlinks pre-d21a0268c).
                        Console.Error.WriteLine($"Skipping unsupported tar entry type {entry.EntryType} for '{entryName}'.");
                        break;
                }
            }

            // Process deferred hard links whose targets were extracted after the link entry.
            foreach (var (path, linkTarget) in deferredHardLinks)
            {
                if (File.Exists(linkTarget))
                {
                    File.Copy(linkTarget, path, overwrite: true);
                }
                else
                {
                    throw new IOException($"Hard link target '{linkTarget}' not found after full archive extraction for '{path}'.");
                }
            }
        }

        private void ErrorExtractingArchive(string archive, string target, string message)
        {
            Console.Error.WriteLine($"Error occured trying to extract archive  '{archive}' to '{target}': {message}.");
        }

        private void ErrorNothingExtracted(string archive, string target)
        {
            Console.Error.WriteLine($"Error occured trying to extract archive. Nothing was extracted from '{archive}' to '{target}.'");
        }

        /// <summary>
        /// This method is used to set the execute permissions bit for the extracted files.
        /// </summary>
        /// <remarks>
        /// The reason for doing this being:
        /// ZIP files and other archive formats may not preserve the executable bit, leading to lost permissions upon extraction.
        /// Files are often transited through Windows OS, where these executable permissions are not natively supported, potentially stripping these permissions.
        /// Without execute permissions, files like node.exe won't run after they are retrieved via DownloadResolver, impacting the build. Given the difficulty in identifying executables, all files are granted execute permissions to avoid this issue.
        /// Also this change also makes the DownloadResolver to be reliably used by our customers.
        /// </remarks>
        private void SetExecutePermissionsForExtractedFiles(string target)
        {
            if (OperatingSystemHelper.IsLinuxOS)
            {
                var files = Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories);

                foreach (var file in files)
                {
                    _ = FileUtilities.SetExecutePermissionIfNeeded(file);
                }
            }
        }
    }
}