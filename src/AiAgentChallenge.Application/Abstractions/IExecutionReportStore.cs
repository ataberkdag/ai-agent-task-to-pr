using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Application.Abstractions;

public interface IExecutionReportStore
{
    Task<ExecutionReport?> FindByIdAsync(
        string id,
        CancellationToken cancellationToken = default);
}
