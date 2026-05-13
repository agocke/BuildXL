// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public class GitRepositoryResolverSettings : ResolverSettings, IGitRepoResolverSettings
    {
        /// <nodoc />
        public GitRepositoryResolverSettings()
        {
            Repositories = new List<IGitRepoSettings>();
        }

        /// <nodoc />
        public GitRepositoryResolverSettings(IGitRepoResolverSettings template, PathRemapper pathRemapper)
            : base(template, pathRemapper)
        {
            Contract.Assume(template != null);
            Contract.Assume(pathRemapper != null);

            Repositories = new List<IGitRepoSettings>(template.Repositories.Count);
            foreach (var repo in template.Repositories)
            {
                Repositories.Add(new GitRepoSettings(repo));
            }
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<IGitRepoSettings> Repositories { get; set; }

        /// <inheritdoc />
        IReadOnlyList<IGitRepoSettings> IGitRepoResolverSettings.Repositories => Repositories;
    }

    /// <nodoc />
    public sealed class GitRepoResolverSettings : GitRepositoryResolverSettings
    {
        /// <nodoc />
        public GitRepoResolverSettings()
        {
        }

        /// <nodoc />
        public GitRepoResolverSettings(IGitRepoResolverSettings template, PathRemapper pathRemapper)
            : base(template, pathRemapper)
        {
        }
    }
}
