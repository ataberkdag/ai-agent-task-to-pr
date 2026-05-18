using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Infrastructure.Ai;

public interface IAiProviderCodeAgent
{
    string ProviderName { get; }

    Task<AiCodeChangeResult> GenerateChangesAsync(
        AgentContext agentContext,
        CancellationToken cancellationToken = default);

    Task<AiCodeChangeResult> RegenerateFormattedChangesAsync(
        AgentContext agentContext,
        AiCodeChangeResult previousResult,
        CancellationToken cancellationToken = default);

    Task<AiCodeChangeResult> GenerateFixForTestFailureAsync(
        AgentContext agentContext,
        AiCodeChangeResult previousResult,
        BuildResult? buildResult,
        TestResult testResult,
        CancellationToken cancellationToken = default);
}
