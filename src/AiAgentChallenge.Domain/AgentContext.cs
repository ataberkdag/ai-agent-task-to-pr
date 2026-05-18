namespace AiAgentChallenge.Domain;

public sealed class AgentContext
{
    public string TaskSummary { get; init; } = string.Empty;

    public string Language { get; init; } = "Unknown";

    public string Framework { get; init; } = "Unknown";

    public string TestFramework { get; init; } = "Unknown";

    public string RepositoryAnalysisSummary { get; init; } = string.Empty;

    public IReadOnlyList<AgentContextFile> SelectedFiles { get; init; } = Array.Empty<AgentContextFile>();
}
