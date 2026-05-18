using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Infrastructure.Analysis;

internal sealed class FrameworkAnalysisResult
{
    public IReadOnlyList<string> RecommendedFiles { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ExistingTestFiles { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ApiEndpointInfo> ApiEndpoints { get; init; } = Array.Empty<ApiEndpointInfo>();

    public IReadOnlyList<CodeSymbolInfo> Symbols { get; init; } = Array.Empty<CodeSymbolInfo>();
}
