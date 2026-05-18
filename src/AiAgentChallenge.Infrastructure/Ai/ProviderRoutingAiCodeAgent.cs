using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Domain;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.Infrastructure.Ai;

public sealed class ProviderRoutingAiCodeAgent : IAiCodeAgent
{
    private readonly IReadOnlyDictionary<string, IAiProviderCodeAgent> _providers;
    private readonly AiOptions _options;

    public ProviderRoutingAiCodeAgent(
        IEnumerable<IAiProviderCodeAgent> providers,
        IOptions<AiOptions> options)
    {
        _providers = providers.ToDictionary(
            provider => provider.ProviderName,
            StringComparer.OrdinalIgnoreCase);
        _options = options.Value;
    }

    public Task<AiCodeChangeResult> GenerateChangesAsync(
        AgentContext agentContext,
        CancellationToken cancellationToken = default)
    {
        return ResolveProvider().GenerateChangesAsync(agentContext, cancellationToken);
    }

    public Task<AiCodeChangeResult> RegenerateFormattedChangesAsync(
        AgentContext agentContext,
        AiCodeChangeResult previousResult,
        CancellationToken cancellationToken = default)
    {
        return ResolveProvider().RegenerateFormattedChangesAsync(
            agentContext,
            previousResult,
            cancellationToken);
    }

    public Task<AiCodeChangeResult> GenerateFixForTestFailureAsync(
        AgentContext agentContext,
        AiCodeChangeResult previousResult,
        BuildResult? buildResult,
        TestResult testResult,
        CancellationToken cancellationToken = default)
    {
        return ResolveProvider().GenerateFixForTestFailureAsync(
            agentContext,
            previousResult,
            buildResult,
            testResult,
            cancellationToken);
    }

    private IAiProviderCodeAgent ResolveProvider()
    {
        var providerName = string.IsNullOrWhiteSpace(_options.Provider)
            ? "OpenAI"
            : _options.Provider;

        if (_providers.TryGetValue(providerName, out var provider))
        {
            return provider;
        }

        throw new InvalidOperationException($"AI provider '{providerName}' is not supported.");
    }
}
