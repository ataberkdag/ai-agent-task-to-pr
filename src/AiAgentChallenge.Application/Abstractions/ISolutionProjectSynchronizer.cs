using AiAgentChallenge.Application.Tasks;
using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Application.Abstractions;

public interface ISolutionProjectSynchronizer
{
    Task<DotNetSolutionBaseline> CaptureBaselineAsync(
        string repositoryPath,
        RepositoryAnalysis repositoryAnalysis,
        CancellationToken cancellationToken = default);

    Task<DotNetSolutionSyncResult> SyncAsync(
        string repositoryPath,
        DotNetSolutionBaseline baseline,
        CancellationToken cancellationToken = default);
}
