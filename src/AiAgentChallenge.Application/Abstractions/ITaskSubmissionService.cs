using AiAgentChallenge.Application.Tasks;

namespace AiAgentChallenge.Application.Abstractions;

public interface ITaskSubmissionService
{
    Task<TaskSubmissionResult> SubmitAsync(
        TaskSubmissionRequest request,
        CancellationToken cancellationToken = default);
}
