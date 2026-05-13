// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Evaluation;
using BuildXL.FrontEnd.Sdk.Mutable;
using BuildXL.FrontEnd.Sdk.Workspaces;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.FrontEnd.GitRepository
{
    /// <summary>
    /// Resolver for Git repositories. Delegates module evaluation to the DScript evaluator.
    /// </summary>
    public sealed class GitRepositoryResolver : IResolver
    {
        private readonly FrontEndStatistics m_frontEndStatistics;
        private readonly EvaluationStatistics m_evaluationStatistics;
        private readonly FrontEndHost m_frontEndHost;
        private readonly FrontEndContext m_context;
        private readonly Script.Tracing.Logger m_logger;
        private GitRepositoryWorkspaceResolver m_workspaceResolver;

        /// <nodoc />
        public string Name { get; private set; }

        /// <nodoc/>
        public GitRepositoryResolver(
            FrontEndStatistics frontEndStatistics,
            EvaluationStatistics evaluationStatistics,
            FrontEndHost frontEndHost,
            FrontEndContext context,
            Script.Tracing.Logger logger,
            string frontEndName)
        {
            Contract.Requires(!string.IsNullOrEmpty(frontEndName));

            Name = frontEndName;
            m_frontEndStatistics = frontEndStatistics;
            m_evaluationStatistics = evaluationStatistics;
            m_frontEndHost = frontEndHost;
            m_context = context;
            m_logger = logger;
        }

        /// <inheritdoc />
        public Task<bool> InitResolverAsync([NotNull] IResolverSettings resolverSettings, object workspaceResolver)
        {
            m_workspaceResolver = workspaceResolver as GitRepositoryWorkspaceResolver;

            if (m_workspaceResolver == null)
            {
                Contract.Assert(false, I($"Wrong type for resolver, expected {nameof(GitRepositoryWorkspaceResolver)} but got {workspaceResolver.GetType().Name}"));
            }

            Name = resolverSettings.Name;

            return Task.FromResult(true);
        }

        /// <inheritdoc />
        public void LogStatistics()
        {
            // Statistics are logged in the FrontEnd.
        }

        /// <inheritdoc />
        public void NotifyEvaluationFinished()
        {
            // Nothing to do
        }

        /// <inheritdoc />
        public async Task<bool?> TryConvertModuleToEvaluationAsync(IModuleRegistry moduleRegistry, ParsedModule module, IWorkspace workspace)
        {
            var maybeModuleDefinition = await m_workspaceResolver.TryGetModuleDefinitionAsync(module.Descriptor);
            if (!maybeModuleDefinition.Succeeded)
            {
                return null;
            }

            var moduleDefinition = maybeModuleDefinition.Result;

            // Each spec in a Git repository module is a regular DScript file, so run regular AST conversion
            foreach (var sourceKv in module.Specs)
            {
                var package = CreatePackage(moduleDefinition);
                var result = await FrontEndUtilities.RunAstConversionAsync(m_frontEndHost, m_context, m_logger, m_frontEndStatistics, package, sourceKv.Key);

                if (!result.Success)
                {
                    return false;
                }

                var moduleData = new UninstantiatedModuleInfo(
                    result.SourceFile,
                    result.Module,
                    result.QualifierSpaceId.IsValid ? result.QualifierSpaceId : m_context.QualifierTable.EmptyQualifierSpaceId);

                m_frontEndHost.ModuleRegistry.AddUninstantiatedModuleInfo(moduleData);
            }

            return true;
        }

        private Package CreatePackage(ModuleDefinition moduleDefinition)
        {
            var moduleDescriptor = moduleDefinition.Descriptor;

            var packageId = PackageId.Create(BuildXL.Utilities.Core.StringId.Create(m_context.StringTable, moduleDescriptor.Name));
            var packageDescriptor = new PackageDescriptor
            {
                Name = moduleDescriptor.Name,
                Main = moduleDefinition.MainFile,
                NameResolutionSemantics = NameResolutionSemantics.ImplicitProjectReferences,
                Publisher = null,
                Version = moduleDescriptor.Version,
                Projects = new List<BuildXL.Utilities.Core.AbsolutePath>(moduleDefinition.Specs),
                ScrubDirectories = new List<BuildXL.Utilities.Core.AbsolutePath>(moduleDefinition.ScrubDirectories)
            };

            return Package.Create(packageId, moduleDefinition.ModuleConfigFile, packageDescriptor, moduleId: moduleDescriptor.Id);
        }

        /// <inheritdoc />
        public async Task<bool?> TryEvaluateModuleAsync([NotNull] IEvaluationScheduler scheduler, [NotNull] ModuleDefinition module, QualifierId qualifierId)
        {
            var maybeModuleDefinition = await m_workspaceResolver.TryGetModuleDefinitionAsync(module.Descriptor);
            if (!maybeModuleDefinition.Succeeded)
            {
                return null;
            }

            // Evaluation is handled by the standard DScript evaluation pipeline since we did AST conversion above
            return true;
        }
    }
}
