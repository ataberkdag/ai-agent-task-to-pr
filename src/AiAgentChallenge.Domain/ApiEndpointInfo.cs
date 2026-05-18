namespace AiAgentChallenge.Domain;

public sealed class ApiEndpointInfo
{
    public string HttpMethod { get; init; } = string.Empty;

    public string Route { get; init; } = string.Empty;

    public string SourceFile { get; init; } = string.Empty;

    public string HandlerName { get; init; } = string.Empty;

    public string Style { get; init; } = string.Empty;
}
