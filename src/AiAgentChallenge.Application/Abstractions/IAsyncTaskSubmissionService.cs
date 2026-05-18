using AiAgentChallenge.Application.Tasks;

namespace AiAgentChallenge.Application.Abstractions;

public interface IAsyncTaskSubmissionService
{
    Task<AsyncTaskSubmissionResult> EnqueueAsync(
        TaskSubmissionRequest request,
        CancellationToken cancellationToken = default);
}
