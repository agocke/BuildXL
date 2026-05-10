// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.FrontEnd.GitRepository.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
    #pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// </summary>
    public enum LogEventId
    {
        None = 0,

        // reserved 11900 .. 11999 for GitRepository front-end
        GitRepoFrontendMissingModuleName = 11900,
        GitRepoFrontendMissingOwner = 11901,
        GitRepoFrontendMissingRepository = 11902,
        GitRepoFrontendMissingCommit = 11903,
        GitRepoFrontendDuplicateModuleName = 11904,
        GitRepoFrontendInvalidCommitHash = 11905,
        GitRepoFrontendDownloadFailed = 11906,
        GitRepoFrontendExtractionFailed = 11907,
        GitRepoFrontendNoModulesFound = 11908,
        GitRepoFrontendHashMismatch = 11909,
        ContextStatistics = 11910,
        BulkStatistic = 11911,
    }
}
