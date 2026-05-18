using AiAgentChallenge.Application.Tasks;
using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Application.Abstractions;

public interface IAgentOrchestrator
{
    Task<ExecutionReport> StartAsync(
        CreateTaskExecutionRequest request,
        CancellationToken cancellationToken = default);
}
