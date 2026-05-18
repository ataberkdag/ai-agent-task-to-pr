namespace AiAgentChallenge.Domain;

public sealed class WorkspaceInfo
{
    public string SafeTaskId { get; init; } = string.Empty;

    public string RunId { get; init; } = string.Empty;

    public string WorkspacePath { get; init; } = string.Empty;

    public string RepositoryPath { get; init; } = string.Empty;
}
