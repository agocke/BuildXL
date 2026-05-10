// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Utilities.Instrumentation.Common;

// Suppress missing XML comments on publicly visible types or members
#pragma warning disable 1591
#nullable enable

namespace BuildXL.FrontEnd.GitRepository.Tracing
{
    /// <summary>
    /// Logging for the GitRepository frontend and resolvers
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    [LoggingDetails("GitRepositoryLogger")]
    public abstract partial class Logger
    {
        private const string ResolverSettingsPrefix = "Error processing GitRepository resolver settings: ";

        /// <summary>
        /// Returns the logger instance
        /// </summary>
        public static Logger Log { get; } = new LoggerImpl();

        // Internal logger will prevent public users from creating an instance of the logger
        internal Logger()
        {
        }

        [GeneratedEvent(
            (ushort)LogEventId.GitRepoFrontendMissingModuleName,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Missing required field 'moduleName' for repository '{owner}/{repository}'.")]
        public abstract void GitRepoFrontendMissingModuleName(LoggingContext context, string owner, string repository);

        [GeneratedEvent(
            (ushort)LogEventId.GitRepoFrontendMissingOwner,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Missing required field 'owner' for module '{moduleName}'.")]
        public abstract void GitRepoFrontendMissingOwner(LoggingContext context, string moduleName);

        [GeneratedEvent(
            (ushort)LogEventId.GitRepoFrontendMissingRepository,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Missing required field 'repository' for module '{moduleName}'.")]
        public abstract void GitRepoFrontendMissingRepository(LoggingContext context, string moduleName);

        [GeneratedEvent(
            (ushort)LogEventId.GitRepoFrontendMissingCommit,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Missing required field 'commit' for module '{moduleName}'. A full commit SHA is required for reproducibility.")]
        public abstract void GitRepoFrontendMissingCommit(LoggingContext context, string moduleName);

        [GeneratedEvent(
            (ushort)LogEventId.GitRepoFrontendDuplicateModuleName,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Duplicate module name '{moduleName}' declared in {kind} resolver named '{name}'.")]
        public abstract void GitRepoFrontendDuplicateModuleName(LoggingContext context, string moduleName, string kind, string name);

        [GeneratedEvent(
            (ushort)LogEventId.GitRepoFrontendInvalidCommitHash,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Invalid commit hash '{commit}' for module '{moduleName}'. Expected a 40-character hex SHA.")]
        public abstract void GitRepoFrontendInvalidCommitHash(LoggingContext context, string moduleName, string commit);

        [GeneratedEvent(
            (ushort)LogEventId.GitRepoFrontendDownloadFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Failed to download repository archive for module '{moduleName}' from '{url}': {reason}")]
        public abstract void GitRepoFrontendDownloadFailed(LoggingContext context, string moduleName, string url, string reason);

        [GeneratedEvent(
            (ushort)LogEventId.GitRepoFrontendExtractionFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Failed to extract repository archive for module '{moduleName}': {reason}")]
        public abstract void GitRepoFrontendExtractionFailed(LoggingContext context, string moduleName, string reason);

        [GeneratedEvent(
            (ushort)LogEventId.GitRepoFrontendNoModulesFound,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (ushort)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message = "GitRepository resolver: No DScript modules (module.config.bm or module.config.dsc) found in repository '{moduleName}' ({owner}/{repository}).")]
        public abstract void GitRepoFrontendNoModulesFound(LoggingContext context, string moduleName, string owner, string repository);

        [GeneratedEvent(
            (ushort)LogEventId.GitRepoFrontendHashMismatch,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Hash mismatch for module '{moduleName}'. Expected '{expectedHash}' but got '{actualHash}'.")]
        public abstract void GitRepoFrontendHashMismatch(LoggingContext context, string moduleName, string expectedHash, string actualHash);

        [GeneratedEvent(
            (ushort)LogEventId.ContextStatistics,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Parser,
            Message = "[GitRepository.{0}] contexts: {1} trees, {2} contexts.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ContextStatistics(LoggingContext context, string name, long contextTrees, long contexts);

        [GeneratedEvent(
            (ushort)LogEventId.BulkStatistic,
            EventGenerators = Generators.Statistics,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.CommonInfrastructure,
            Message = "N/A",
            Keywords = (int)Keywords.Diagnostics)]
        public abstract void BulkStatistic(LoggingContext context, IDictionary<string, long> statistics);
    }
}
