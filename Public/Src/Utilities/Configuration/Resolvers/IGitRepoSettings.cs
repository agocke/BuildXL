// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Settings for a single Git repository to download
    /// </summary>
    public partial interface IGitRepoSettings
    {
        /// <summary>
        /// The name of the module to expose. This is used as the module name for importFrom().
        /// </summary>
        string ModuleName { get; }

        /// <summary>
        /// The GitHub repository owner (user or organization)
        /// </summary>
        string Owner { get; }

        /// <summary>
        /// The GitHub repository name
        /// </summary>
        string Repository { get; }

        /// <summary>
        /// The full commit SHA to download. Required for reproducibility.
        /// </summary>
        string Commit { get; }

        /// <summary>
        /// Optional hash of the downloaded archive to ensure integrity. When specified the download is validated against this hash.
        /// </summary>
        string Hash { get; }

        /// <summary>
        /// Optional list of module names to expose from this repository. When specified, only modules whose
        /// <c>name</c> field matches one of these entries are surfaced to the workspace. All other
        /// <c>module.config.dsc</c> / <c>module.config.bm</c> files in the fetched repository are ignored,
        /// which prevents stray <c>.dsc</c> files (e.g., test modules or internal tooling) with unresolvable
        /// imports from being parsed. When omitted, every module config discovered in the repository is exposed.
        /// </summary>
        IReadOnlyList<string> Modules { get; }
    }
}
