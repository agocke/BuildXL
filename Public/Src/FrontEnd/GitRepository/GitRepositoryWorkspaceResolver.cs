// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.GitRepository.Tracing;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using TypeScript.Net.DScript;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.GitRepository
{
    /// <summary>
    /// A workspace module resolver that downloads Git repositories and discovers DScript modules within them.
    /// </summary>
    /// <remarks>
    /// This resolver follows the NuGet resolver pattern: eagerly download repositories during workspace resolution
    /// (before graph construction), then delegate to an embedded <see cref="WorkspaceSourceModuleResolver"/>
    /// for module discovery from the extracted content.
    /// </remarks>
    public sealed class GitRepositoryWorkspaceResolver : IWorkspaceModuleResolver
    {
        /// <inheritdoc />
        public string Kind => KnownResolverKind.GitRepositoryResolverKind;

        /// <inheritdoc />
        public string Name { get; private set; }

        private FrontEndContext m_context;
        private FrontEndHost m_host;
        private IConfiguration m_configuration;
        private IGitRepoResolverSettings m_resolverSettings;

        // Embedded source resolver for module discovery
        private WorkspaceSourceModuleResolver m_embeddedResolver;

        // Lazy initialization of download + module discovery
        private CachedTask<Possible<bool>> m_initResult = CachedTask<Possible<bool>>.Create();

        // Resolved module data
        private readonly Dictionary<string, GitRepositoryData> m_repositories = new(StringComparer.Ordinal);

        /// <inheritdoc />
        public bool TryInitialize(
            [NotNull] FrontEndHost host,
            [NotNull] FrontEndContext context,
            [NotNull] IConfiguration configuration,
            [NotNull] IResolverSettings resolverSettings)
        {
            Contract.Requires(host != null);
            Contract.Requires(context != null);
            Contract.Requires(configuration != null);
            Contract.Requires(resolverSettings != null);

            var settings = resolverSettings as IGitRepoResolverSettings;
            Contract.Assert(settings != null);

            m_context = context;
            m_host = host;
            m_configuration = configuration;
            m_resolverSettings = settings;
            Name = resolverSettings.Name;

            m_embeddedResolver = new WorkspaceSourceModuleResolver(context.StringTable, new FrontEndStatistics(), logger: null);

            var resolverFolder = host.GetFolderForFrontEnd(resolverSettings.Name ?? Kind);

            // Validate settings
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var repoSettings in settings.Repositories)
            {
                if (!ValidateRepoSettings(repoSettings, seen))
                {
                    return false;
                }

                var data = new GitRepositoryData(context, repoSettings, resolverFolder);
                m_repositories.Add(repoSettings.ModuleName, data);
                seen.Add(repoSettings.ModuleName);
            }

            return true;
        }

        private bool ValidateRepoSettings(IGitRepoSettings repoSettings, HashSet<string> seen)
        {
            if (string.IsNullOrEmpty(repoSettings.ModuleName))
            {
                Logger.Log.GitRepoFrontendMissingModuleName(m_context.LoggingContext, repoSettings.Owner ?? "", repoSettings.Repository ?? "");
                return false;
            }

            if (string.IsNullOrEmpty(repoSettings.Owner))
            {
                Logger.Log.GitRepoFrontendMissingOwner(m_context.LoggingContext, repoSettings.ModuleName);
                return false;
            }

            if (string.IsNullOrEmpty(repoSettings.Repository))
            {
                Logger.Log.GitRepoFrontendMissingRepository(m_context.LoggingContext, repoSettings.ModuleName);
                return false;
            }

            if (string.IsNullOrEmpty(repoSettings.Commit))
            {
                Logger.Log.GitRepoFrontendMissingCommit(m_context.LoggingContext, repoSettings.ModuleName);
                return false;
            }

            // Validate commit is a 40-char hex SHA
            if (repoSettings.Commit.Length != 40 || !repoSettings.Commit.All(c => Uri.IsHexDigit(c)))
            {
                Logger.Log.GitRepoFrontendInvalidCommitHash(m_context.LoggingContext, repoSettings.ModuleName, repoSettings.Commit);
                return false;
            }

            if (seen.Contains(repoSettings.ModuleName))
            {
                Logger.Log.GitRepoFrontendDuplicateModuleName(m_context.LoggingContext, repoSettings.ModuleName, Kind, Name);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Ensures all repositories are downloaded and extracted, and the embedded resolver is initialized.
        /// </summary>
        private Task<Possible<bool>> EnsureDownloadedAndInitializedAsync()
        {
            return m_initResult.GetOrCreate(this, static self => self.DownloadAndInitializeAsync());
        }

        private async Task<Possible<bool>> DownloadAndInitializeAsync()
        {
            // Download and extract each repository
            foreach (var kvp in m_repositories)
            {
                var data = kvp.Value;
                if (!await DownloadAndExtractAsync(data))
                {
                    return false;
                }
            }

            // Initialize the embedded source resolver with the extracted roots
            // The embedded resolver will discover module.config.bm/dsc files from these roots
            var sourceResolverSettings = CreateEmbeddedResolverSettings();
            if (!m_embeddedResolver.TryInitialize(m_host, m_context, m_configuration, sourceResolverSettings))
            {
                return false;
            }

            return true;
        }

        private async Task<bool> DownloadAndExtractAsync(GitRepositoryData data)
        {
            var extractedRoot = data.ExtractedRoot.ToString(m_context.PathTable);

            // Skip if already extracted (cache hit)
            if (Directory.Exists(extractedRoot) && Directory.EnumerateFileSystemEntries(extractedRoot).Any())
            {
                return true;
            }

            // Download the archive
            var archiveUrl = data.ArchiveUrl;
            ContentHash? expectedHash = data.ContentHash;

            var possibleHash = await m_host.DownloadFile(archiveUrl, data.ArchivePath, expectedHash, $"GitRepository:{data.Settings.ModuleName}");
            if (!possibleHash.Succeeded)
            {
                Logger.Log.GitRepoFrontendDownloadFailed(m_context.LoggingContext, data.Settings.ModuleName, archiveUrl, possibleHash.Failure.Describe());
                return false;
            }

            // Extract the archive
            try
            {
                var archivePath = data.ArchivePath.ToString(m_context.PathTable);
                Directory.CreateDirectory(extractedRoot);

                ExtractTarGz(archivePath, extractedRoot, stripTopLevelDirectory: true);
            }
            catch (Exception ex)
            {
                Logger.Log.GitRepoFrontendExtractionFailed(m_context.LoggingContext, data.Settings.ModuleName, ex.ToString());
                return false;
            }

            return true;
        }

        /// <summary>
        /// Extracts a .tar.gz archive to the target directory.
        /// When stripTopLevelDirectory is true, removes the top-level directory that GitHub includes in archives
        /// (e.g., "repo-commitsha/").
        /// </summary>
        private static void ExtractTarGz(string archivePath, string targetDirectory, bool stripTopLevelDirectory)
        {
            using var fileStream = File.OpenRead(archivePath);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            using var reader = new TarReader(gzipStream);

            string topLevelPrefix = null;

            while (reader.GetNextEntry() is TarEntry entry)
            {
                var entryName = entry.Name;

                if (stripTopLevelDirectory)
                {
                    // Determine the top-level directory from the first entry
                    if (topLevelPrefix == null)
                    {
                        var slashIndex = entryName.IndexOf('/');
                        if (slashIndex > 0)
                        {
                            topLevelPrefix = entryName.Substring(0, slashIndex + 1);
                        }
                    }

                    if (topLevelPrefix != null && entryName.StartsWith(topLevelPrefix, StringComparison.Ordinal))
                    {
                        entryName = entryName.Substring(topLevelPrefix.Length);
                    }

                    // Skip the top-level directory entry itself
                    if (string.IsNullOrEmpty(entryName))
                    {
                        continue;
                    }
                }

                var targetPath = Path.Combine(targetDirectory, entryName.Replace('/', Path.DirectorySeparatorChar));

                switch (entry.EntryType)
                {
                    case TarEntryType.Directory:
                        Directory.CreateDirectory(targetPath);
                        break;

                    case TarEntryType.SymbolicLink:
                        Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                        var symlinkResult = FileUtilities.TryCreateSymbolicLink(targetPath, entry.LinkName, isTargetFile: true);
                        if (!symlinkResult.Succeeded)
                        {
                            throw new IOException($"Failed to create symlink '{targetPath}' -> '{entry.LinkName}': {symlinkResult.Failure.Describe()}");
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
                        break;
                }
            }
        }

        private IResolverSettings CreateEmbeddedResolverSettings()
        {
            var modules = new List<DiscriminatingUnion<AbsolutePath, IInlineModuleDefinition>>();

            foreach (var data in m_repositories.Values)
            {
                var extractedRoot = data.ExtractedRoot.ToString(m_context.PathTable);
                var moduleConfigs = Directory.EnumerateFiles(extractedRoot, "*config*", SearchOption.AllDirectories)
                    .Where(candidate => IsModuleConfigurationFile(Path.GetFileName(candidate)))
                    .ToList();

                // If the user requested a specific subset of modules, narrow down to those whose
                // declared name matches the filter. This prevents stray .dsc files in test modules
                // or internal tooling from being parsed (and potentially crashing) in the consumer.
                var requestedModules = data.Settings.Modules;
                var hasFilter = requestedModules != null && requestedModules.Count > 0;

                HashSet<string> requestedSet = null;
                HashSet<string> matchedSet = null;
                Dictionary<string, string> discoveredNameToPath = null;

                if (hasFilter)
                {
                    requestedSet = new HashSet<string>(requestedModules, StringComparer.Ordinal);
                    matchedSet = new HashSet<string>(StringComparer.Ordinal);
                    discoveredNameToPath = new Dictionary<string, string>(StringComparer.Ordinal);
                }

                var foundModule = false;
                foreach (var moduleConfig in moduleConfigs)
                {
                    if (hasFilter)
                    {
                        if (!TryExtractModuleName(moduleConfig, out var declaredName))
                        {
                            Logger.Log.GitRepoFrontendFailedToParseModuleConfig(
                                m_context.LoggingContext,
                                data.Settings.ModuleName,
                                moduleConfig);
                            continue;
                        }

                        discoveredNameToPath[declaredName] = moduleConfig;

                        if (!requestedSet.Contains(declaredName))
                        {
                            continue;
                        }

                        matchedSet.Add(declaredName);
                    }

                    if (AbsolutePath.TryCreate(m_context.PathTable, moduleConfig, out var result))
                    {
                        modules.Add(new DiscriminatingUnion<AbsolutePath, IInlineModuleDefinition>(result));
                        foundModule = true;
                    }
                }

                if (hasFilter)
                {
                    var missing = requestedSet.Where(name => !matchedSet.Contains(name)).ToList();
                    if (missing.Count > 0)
                    {
                        var available = string.Join(", ", discoveredNameToPath.Keys.OrderBy(n => n, StringComparer.Ordinal));
                        foreach (var name in missing)
                        {
                            Logger.Log.GitRepoFrontendRequestedModuleNotFound(
                                m_context.LoggingContext,
                                data.Settings.ModuleName,
                                data.Settings.Owner,
                                data.Settings.Repository,
                                name,
                                available);
                        }
                    }
                }

                if (!foundModule)
                {
                    Logger.Log.GitRepoFrontendNoModulesFound(
                        m_context.LoggingContext,
                        data.Settings.ModuleName,
                        data.Settings.Owner,
                        data.Settings.Repository);
                }
            }

            // We create a settings object that points the embedded source resolver at all discovered
            // module configuration files inside the extracted repositories.
            var settings = new BuildXL.Utilities.Configuration.Mutable.DScriptResolverSettings
            {
                Name = Name,
                Kind = KnownResolverKind.DScriptResolverKind,
                Location = m_resolverSettings.Location,
                Modules = modules,
            };

            return settings;
        }

        private static bool IsModuleConfigurationFile(string fileName)
        {
            return fileName.Equals("package.config.dsc", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("module.config.dsc", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("module.config.bm", StringComparison.OrdinalIgnoreCase);
        }

        // Matches:  name : "Foo.Bar"   or   name: 'Foo.Bar'
        // The module config files are conventionally small and use this exact shape.
        private static readonly System.Text.RegularExpressions.Regex s_moduleNameRegex =
            new System.Text.RegularExpressions.Regex(
                @"\bname\s*:\s*[""']([^""']+)[""']",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Lightweight extraction of the declared module name from a <c>module.config.dsc</c> /
        /// <c>module.config.bm</c> / <c>package.config.dsc</c> file. Returns false when no name
        /// declaration can be located.
        /// </summary>
        internal static bool TryExtractModuleName(string moduleConfigPath, out string moduleName)
        {
            moduleName = null;
            try
            {
                string contents = File.ReadAllText(moduleConfigPath);
                var match = s_moduleNameRegex.Match(contents);
                if (match.Success)
                {
                    moduleName = match.Groups[1].Value;
                    return true;
                }
            }
            catch (IOException)
            {
            }
            catch (System.UnauthorizedAccessException)
            {
            }

            return false;
        }

        /// <inheritdoc />
        public string DescribeExtent()
        {
            return string.Join(", ", m_repositories.Keys);
        }

        /// <inheritdoc />
        public async ValueTask<Possible<HashSet<ModuleDescriptor>>> GetAllKnownModuleDescriptorsAsync()
        {
            var maybeInit = await EnsureDownloadedAndInitializedAsync();
            if (!maybeInit.Succeeded)
            {
                return maybeInit.Failure;
            }

            return await m_embeddedResolver.GetAllKnownModuleDescriptorsAsync();
        }

        /// <inheritdoc />
        public ISourceFile[] GetAllModuleConfigurationFiles()
        {
            // Delegate to the embedded resolver after initialization
            return m_embeddedResolver.GetAllModuleConfigurationFiles();
        }

        /// <inheritdoc />
        public Task ReinitializeResolver()
        {
            // Not applicable — repository content doesn't change during a build
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async ValueTask<Possible<ModuleDefinition>> TryGetModuleDefinitionAsync(ModuleDescriptor moduleDescriptor)
        {
            var maybeInit = await EnsureDownloadedAndInitializedAsync();
            if (!maybeInit.Succeeded)
            {
                return maybeInit.Failure;
            }

            return await m_embeddedResolver.TryGetModuleDefinitionAsync(moduleDescriptor);
        }

        /// <inheritdoc />
        public async ValueTask<Possible<IReadOnlyCollection<ModuleDescriptor>>> TryGetModuleDescriptorsAsync(ModuleReferenceWithProvenance moduleReference)
        {
            var maybeInit = await EnsureDownloadedAndInitializedAsync();
            if (!maybeInit.Succeeded)
            {
                return maybeInit.Failure;
            }

            return await m_embeddedResolver.TryGetModuleDescriptorsAsync(moduleReference);
        }

        /// <inheritdoc />
        public async ValueTask<Possible<ModuleDescriptor>> TryGetOwningModuleDescriptorAsync(AbsolutePath specPath)
        {
            var maybeInit = await EnsureDownloadedAndInitializedAsync();
            if (!maybeInit.Succeeded)
            {
                return maybeInit.Failure;
            }

            return await m_embeddedResolver.TryGetOwningModuleDescriptorAsync(specPath);
        }

        /// <inheritdoc />
        public async Task<Possible<ISourceFile>> TryParseAsync(AbsolutePath pathToParse, AbsolutePath moduleOrConfigPathPromptingParse, ParsingOptions parsingOption = null)
        {
            var maybeInit = await EnsureDownloadedAndInitializedAsync();
            if (!maybeInit.Succeeded)
            {
                return maybeInit.Failure;
            }

            return await m_embeddedResolver.TryParseAsync(pathToParse, moduleOrConfigPathPromptingParse, parsingOption);
        }
    }
}
