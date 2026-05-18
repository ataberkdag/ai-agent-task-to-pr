namespace AiAgentChallenge.Application.Abstractions;

public interface IGitHubRepositoryParser
{
    (string Owner, string RepositoryName) Parse(string repositoryUrl);
}
