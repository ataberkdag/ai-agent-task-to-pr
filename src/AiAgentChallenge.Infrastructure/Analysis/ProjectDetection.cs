namespace AiAgentChallenge.Infrastructure.Analysis;

internal sealed class ProjectDetection
{
    public string Language { get; init; } = "Unknown";

    public string Framework { get; init; } = "Unknown";

    public string BuildTool { get; init; } = "Unknown";

    public string TestCommand { get; init; } = string.Empty;

    public string TestFramework { get; init; } = "Unknown";

    public IReadOnlyList<string> DetectedProjectFiles { get; init; } = Array.Empty<string>();
}
