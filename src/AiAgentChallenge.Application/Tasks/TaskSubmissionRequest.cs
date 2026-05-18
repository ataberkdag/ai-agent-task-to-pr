namespace AiAgentChallenge.Application.Tasks;

public sealed class TaskSubmissionRequest
{
    public Guid ExecutionId { get; init; }

    public string TaskId { get; init; } = string.Empty;

    public string TraceId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;
}
