// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.GitRepository.Tracing;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces.Core;

namespace BuildXL.FrontEnd.GitRepository
{
    /// <summary>
    /// GitRepository resolver frontend
    /// </summary>
    public sealed class GitRepositoryFrontEnd : FrontEnd<GitRepositoryWorkspaceResolver>
    {
        private readonly Script.Tracing.Logger m_logger;
        private readonly FrontEndStatistics m_frontEndStatistics;
        private readonly EvaluationStatistics m_evaluationStatistics;

        /// <summary>
        /// Gets or sets the name of the front-end.
        /// </summary>
        public override string Name => KnownResolverKind.GitRepositoryResolverKind;

        /// <inheritdoc />
        public override bool ShouldRestrictBuildParameters { get; } = false;

        /// <nodoc/>
        public GitRepositoryFrontEnd()
        {
            m_logger = Script.Tracing.Logger.CreateLogger(preserveLogEvents: true);
            m_frontEndStatistics = new FrontEndStatistics();
            m_evaluationStatistics = new EvaluationStatistics();
        }

        /// <inheritdoc />
        public override IReadOnlyCollection<string> SupportedResolvers { get; } = new[] { KnownResolverKind.GitRepositoryResolverKind };

        /// <inheritdoc />
        public override IResolver CreateResolver([NotNull] string kind)
        {
            Contract.Requires(SupportedResolvers.Contains(kind));

            return new GitRepositoryResolver(
                m_frontEndStatistics,
                m_evaluationStatistics,
                Host,
                Context,
                m_logger,
                Name);
        }

        /// <inheritdoc />
        public override void LogStatistics(Dictionary<string, long> statistics)
        {
            if (Context == null)
            {
                return;
            }

            Logger.Log.ContextStatistics(Context.LoggingContext, Name, m_evaluationStatistics.ContextTrees,
                m_evaluationStatistics.Contexts);

            var frontEndStatistics = new Dictionary<string, long>
            {
                { "GitRepository.AggregatedAstConversionCount", (long)m_frontEndStatistics.SpecAstConversion.Count },
                { "GitRepository.AggregatedAstConversionDurationMs", (long)m_frontEndStatistics.SpecAstConversion.AggregateDuration.TotalMilliseconds },
                { "GitRepository.AggregatedAstSerializationDurationMs", (long)m_frontEndStatistics.SpecAstSerialization.AggregateDuration.TotalMilliseconds },
                { "GitRepository.AggregatedAstDeserializationDurationMs", (long)m_frontEndStatistics.SpecAstDeserialization.AggregateDuration.TotalMilliseconds },
            };

            Logger.Log.BulkStatistic(Context.LoggingContext, frontEndStatistics);
        }
    }
}
