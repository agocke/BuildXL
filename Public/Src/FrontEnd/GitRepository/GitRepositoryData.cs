// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;

namespace BuildXL.FrontEnd.GitRepository
{
    /// <summary>
    /// Extracted data for a single Git repository to download
    /// </summary>
    public sealed class GitRepositoryData
    {
        /// <summary>
        /// The settings as defined in the resolver
        /// </summary>
        public IGitRepoSettings Settings { get; }

        /// <summary>
        /// The URL to download the archive from (GitHub's tarball endpoint)
        /// </summary>
        public string ArchiveUrl { get; }

        /// <summary>
        /// Path where the archive will be downloaded
        /// </summary>
        public AbsolutePath ArchivePath { get; }

        /// <summary>
        /// Root path where the archive will be extracted
        /// </summary>
        public AbsolutePath ExtractedRoot { get; }

        /// <summary>
        /// Optional content hash for integrity verification
        /// </summary>
        public ContentHash? ContentHash { get; }

        /// <nodoc />
        public GitRepositoryData(FrontEndContext context, IGitRepoSettings settings, AbsolutePath resolverRoot)
        {
            Contract.Requires(context != null);
            Contract.Requires(settings != null);
            Contract.Requires(resolverRoot.IsValid);

            Settings = settings;

            // GitHub provides tarball archives at this URL pattern
            ArchiveUrl = $"https://github.com/{settings.Owner}/{settings.Repository}/archive/{settings.Commit}.tar.gz";

            // Use a deterministic path based on owner/repo/commit
            var repoFolder = resolverRoot
                .Combine(context.PathTable, settings.Owner)
                .Combine(context.PathTable, settings.Repository)
                .Combine(context.PathTable, settings.Commit);

            ArchivePath = repoFolder.Combine(context.PathTable, "archive.tar.gz");
            ExtractedRoot = repoFolder.Combine(context.PathTable, "src");

            // Parse hash if provided
            if (!string.IsNullOrEmpty(settings.Hash))
            {
                if (Cache.ContentStore.Hashing.ContentHash.TryParse(settings.Hash, out var hash))
                {
                    ContentHash = hash;
                }
            }
        }
    }
}
