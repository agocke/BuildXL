// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class GitRepoSettings : IGitRepoSettings
    {
        /// <nodoc />
        public GitRepoSettings()
        {
        }

        /// <nodoc />
        public GitRepoSettings(IGitRepoSettings template)
        {
            ModuleName = template.ModuleName;
            Owner = template.Owner;
            Repository = template.Repository;
            Commit = template.Commit;
            Hash = template.Hash;
            Modules = template.Modules?.ToList();
        }

        /// <inheritdoc />
        public string ModuleName { get; set; }

        /// <inheritdoc />
        public string Owner { get; set; }

        /// <inheritdoc />
        public string Repository { get; set; }

        /// <inheritdoc />
        public string Commit { get; set; }

        /// <inheritdoc />
        public string Hash { get; set; }

        /// <inheritdoc />
        public IReadOnlyList<string> Modules { get; set; }
    }
}
