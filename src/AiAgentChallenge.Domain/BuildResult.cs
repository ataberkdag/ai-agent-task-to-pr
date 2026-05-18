namespace AiAgentChallenge.Domain;

public sealed class BuildResult
{
    public string Command { get; init; } = string.Empty;

    public BuildExecutionStatus Status { get; init; }

    public int ExitCode { get; init; }

    public TimeSpan Duration { get; init; }

    public string Stdout { get; init; } = string.Empty;

    public IReadOnlyList<string> StdoutLines { get; init; } = Array.Empty<string>();

    public string Stderr { get; init; } = string.Empty;

    public int AttemptNumber { get; init; }
}
