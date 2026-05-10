// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Settings for the GitRepository resolver
    /// </summary>
    public partial interface IGitRepoResolverSettings : IResolverSettings
    {
        /// <summary>
        /// Git repositories to download and integrate as DScript modules
        /// </summary>
        IReadOnlyList<IGitRepoSettings> Repositories { get; }
    }
}
