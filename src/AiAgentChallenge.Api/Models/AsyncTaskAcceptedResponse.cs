namespace AiAgentChallenge.Api.Models;

public sealed class AsyncTaskAcceptedResponse
{
    public string Id { get; init; } = string.Empty;

    public string TaskId { get; init; } = string.Empty;

    public string TraceId { get; init; } = string.Empty;

    public DateTimeOffset QueuedAtUtc { get; init; }

    public string Message { get; init; } = string.Empty;
}
