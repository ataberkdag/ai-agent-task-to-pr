using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Application.Abstractions;

public interface IAgentContextBuilder
{
    Task<AgentContext> BuildAsync(
        string repositoryPath,
        ParsedTask parsedTask,
        RepositoryAnalysis repositoryAnalysis,
        CancellationToken cancellationToken = default);
}
