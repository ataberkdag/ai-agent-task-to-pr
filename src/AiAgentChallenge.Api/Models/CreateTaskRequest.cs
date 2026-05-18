namespace AiAgentChallenge.Api.Models;

public sealed class CreateTaskRequest
{
    public string TaskId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;
}
