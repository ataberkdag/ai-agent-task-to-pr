namespace AiAgentChallenge.Domain;

public sealed class RepositoryAnalysis
{
    public string Language { get; init; } = "Unknown";

    public string Framework { get; init; } = "Unknown";

    public string BuildTool { get; init; } = "Unknown";

    public string TestCommand { get; init; } = string.Empty;

    public string TestFramework { get; init; } = "Unknown";

    public IReadOnlyList<string> AvailableTestLibraries { get; init; } = Array.Empty<string>();

    public string TargetFramework { get; init; } = string.Empty;

    public IReadOnlyList<string> TargetFrameworks { get; init; } = Array.Empty<string>();

    public string LangVersion { get; init; } = string.Empty;

    public IReadOnlyList<string> ProjectFiles { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RelevantFiles { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ExistingTestFiles { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ApiEndpointInfo> ApiEndpoints { get; init; } = Array.Empty<ApiEndpointInfo>();

    public IReadOnlyList<CodeSymbolInfo> Symbols { get; init; } = Array.Empty<CodeSymbolInfo>();
}
