using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Application.Tasks;

public sealed class CreateTaskExecutionRequest
{
    public Guid ExecutionId { get; init; }

    public string TaskId { get; init; } = string.Empty;

    public string TraceId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public ParsedTask ParsedTask { get; init; } = new();
}
