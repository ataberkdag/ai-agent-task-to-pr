using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Application.Tasks;
using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Infrastructure.Projects;

internal sealed class NoOpSolutionProjectSynchronizer : ISolutionProjectSynchronizer
{
    public Task<DotNetSolutionBaseline> CaptureBaselineAsync(
        string repositoryPath,
        RepositoryAnalysis repositoryAnalysis,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(DotNetSolutionBaseline.Unsupported("Solution sync service is not configured."));
    }

    public Task<DotNetSolutionSyncResult> SyncAsync(
        string repositoryPath,
        DotNetSolutionBaseline baseline,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(DotNetSolutionSyncResult.Success(
            baseline.SolutionPath,
            Array.Empty<string>(),
            Array.Empty<string>(),
            baseline.Message));
    }
}
