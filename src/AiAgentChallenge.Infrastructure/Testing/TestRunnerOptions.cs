namespace AiAgentChallenge.Infrastructure.Testing;

public sealed class TestRunnerOptions
{
    public int TimeoutSeconds { get; init; } = 120;

    public int MaxOutputChars { get; init; } = 8000;
}
