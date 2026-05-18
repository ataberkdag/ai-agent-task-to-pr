using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Application.Abstractions;

public interface IRepositoryAnalyzer
{
    Task<RepositoryAnalysis> AnalyzeAsync(
        string repositoryPath,
        ParsedTask parsedTask,
        CancellationToken cancellationToken = default);
}
