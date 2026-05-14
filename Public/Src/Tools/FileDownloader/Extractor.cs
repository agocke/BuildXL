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
        /// Extracts a tar stream to the target directory, handling regular files, directories, and symbolic links.
        /// </summary>
        private static void ExtractTar(Stream tarStream, string targetDirectory)
        {
            using var reader = new TarReader(tarStream);

            while (reader.GetNextEntry() is TarEntry entry)
            {
                // Sanitize: prevent path traversal attacks
                var entryName = entry.Name;
                if (entryName.Contains(".."))
                {
                    continue;
                }

                var targetPath = Path.Combine(targetDirectory, entryName.Replace('/', Path.DirectorySeparatorChar));

                switch (entry.EntryType)
                {
                    case TarEntryType.Directory:
                        Directory.CreateDirectory(targetPath);
                        break;

                    case TarEntryType.SymbolicLink:
                        Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                        // entry.LinkName contains the relative or absolute symlink target
                        FileUtilities.TryCreateSymbolicLink(targetPath, entry.LinkName, isTargetFile: true);
                        break;

                    case TarEntryType.RegularFile:
                    case TarEntryType.V7RegularFile:
                        Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                        using (var outputStream = File.Create(targetPath))
                        {
                            entry.DataStream?.CopyTo(outputStream);
                        }
                        break;

                    // Skip other entry types (hard links, device nodes, etc.)
                    default:
                        break;
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