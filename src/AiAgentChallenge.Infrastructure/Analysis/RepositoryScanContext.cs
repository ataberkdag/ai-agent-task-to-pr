using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Infrastructure.Analysis;

internal sealed class RepositoryScanContext
{
    public string RepositoryPath { get; init; } = string.Empty;

    public ParsedTask ParsedTask { get; init; } = new();

    public ProjectDetection Detection { get; init; } = new();

    public IReadOnlyList<AnalyzedRepositoryFile> Files { get; init; } = Array.Empty<AnalyzedRepositoryFile>();

    public IReadOnlyCollection<string> Keywords { get; init; } = Array.Empty<string>();
}
