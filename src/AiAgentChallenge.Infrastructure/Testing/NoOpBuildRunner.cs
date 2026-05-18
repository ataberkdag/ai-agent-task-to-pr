using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Infrastructure.Testing;

internal sealed class NoOpBuildRunner : IBuildRunner
{
    public Task<BuildResult> RunAsync(
        string repositoryPath,
        RepositoryAnalysis repositoryAnalysis,
        int attemptNumber,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new BuildResult
        {
            Command = string.Empty,
            Status = BuildExecutionStatus.Skipped,
            ExitCode = -1,
            Duration = TimeSpan.Zero,
            Stdout = string.Empty,
            Stderr = "Build validation was not configured.",
            AttemptNumber = attemptNumber
        });
    }
}
