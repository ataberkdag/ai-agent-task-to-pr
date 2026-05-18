namespace AiAgentChallenge.Domain;

public sealed class AiUsageInfo
{
    public string Model { get; init; } = string.Empty;

    public int? InputTokens { get; init; }

    public int? OutputTokens { get; init; }

    public int? TotalTokens { get; init; }
}
