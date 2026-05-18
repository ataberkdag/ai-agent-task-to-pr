namespace AiAgentChallenge.Domain;

public sealed class ParsedTask
{
    public string TaskId { get; init; } = string.Empty;

    public string RepositoryUrl { get; init; } = string.Empty;

    public string BaseBranch { get; init; } = string.Empty;

    public string Requirement { get; init; } = string.Empty;

    public IReadOnlyList<string> AcceptanceCriteria { get; init; } = Array.Empty<string>();
}
