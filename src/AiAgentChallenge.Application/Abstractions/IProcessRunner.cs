using AiAgentChallenge.Application.Tasks;

namespace AiAgentChallenge.Application.Abstractions;

public interface IProcessRunner
{
    Task<ProcessExecutionResult> RunAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken = default);
}
