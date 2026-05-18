namespace AiAgentChallenge.Domain;

public sealed class CodeParameterInfo
{
    public string Name { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public int Ordinal { get; init; }

    public bool IsOptional { get; init; }
}
