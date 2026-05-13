// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
    }
}
