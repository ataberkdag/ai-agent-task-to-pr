namespace AiAgentChallenge.Infrastructure.Git;

public sealed class GitOptions
{
    public int CloneTimeoutSeconds { get; init; } = 120;

    public int PushTimeoutSeconds { get; init; } = 120;
}
