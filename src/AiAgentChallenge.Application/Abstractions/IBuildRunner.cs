using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Application.Abstractions;

public interface IBuildRunner
{
    Task<BuildResult> RunAsync(
        string repositoryPath,
        RepositoryAnalysis repositoryAnalysis,
        int attemptNumber,
        CancellationToken cancellationToken = default);
}
