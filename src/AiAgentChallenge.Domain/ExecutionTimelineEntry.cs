namespace AiAgentChallenge.Domain;

public sealed class ExecutionTimelineEntry
{
    public string Step { get; init; } = string.Empty;

    public ExecutionTimelineStatus Status { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset? FinishedAtUtc { get; init; }

    public long? DurationMs { get; init; }

    public string Message { get; init; } = string.Empty;
}
