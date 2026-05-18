using AiAgentChallenge.Domain;
using AiAgentChallenge.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class ProviderRoutingAiCodeAgentTests
{
    [Fact]
    public async Task GenerateChangesAsync_UsesOpenAiProvider()
    {
        var openAiProvider = new FakeProvider("OpenAI", "openai");
        var geminiProvider = new FakeProvider("Gemini", "gemini");
        var router = new ProviderRoutingAiCodeAgent(
            new IAiProviderCodeAgent[] { openAiProvider, geminiProvider },
            Options.Create(new AiOptions { Provider = "OpenAI" }));

        var result = await router.GenerateChangesAsync(CreateContext());

        Assert.Equal("openai", result.Summary);
        Assert.Equal(1, openAiProvider.GenerateChangesCalls);
        Assert.Equal(0, geminiProvider.GenerateChangesCalls);
    }

    [Fact]
    public async Task GenerateChangesAsync_UsesGeminiProvider()
    {
        var openAiProvider = new FakeProvider("OpenAI", "openai");
        var geminiProvider = new FakeProvider("Gemini", "gemini");
        var router = new ProviderRoutingAiCodeAgent(
            new IAiProviderCodeAgent[] { openAiProvider, geminiProvider },
            Options.Create(new AiOptions { Provider = "Gemini" }));

        var result = await router.GenerateChangesAsync(CreateContext());

        Assert.Equal("gemini", result.Summary);
        Assert.Equal(0, openAiProvider.GenerateChangesCalls);
        Assert.Equal(1, geminiProvider.GenerateChangesCalls);
    }

    [Fact]
    public async Task GenerateChangesAsync_ThrowsForUnsupportedProvider()
    {
        var router = new ProviderRoutingAiCodeAgent(
            new IAiProviderCodeAgent[] { new FakeProvider("OpenAI", "openai") },
            Options.Create(new AiOptions { Provider = "Unsupported" }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => router.GenerateChangesAsync(CreateContext()));

        Assert.Equal("AI provider 'Unsupported' is not supported.", exception.Message);
    }

    private static AgentContext CreateContext()
    {
        return new AgentContext
        {
            TaskSummary = "Requirement: Add validation",
            RepositoryAnalysisSummary = "Language: C#",
            SelectedFiles = Array.Empty<AgentContextFile>()
        };
    }

    private sealed class FakeProvider : IAiProviderCodeAgent
    {
        private readonly string _summary;

        public FakeProvider(string providerName, string summary)
        {
            ProviderName = providerName;
            _summary = summary;
        }

        public string ProviderName { get; }

        public int GenerateChangesCalls { get; private set; }

        public Task<AiCodeChangeResult> GenerateChangesAsync(AgentContext agentContext, CancellationToken cancellationToken = default)
        {
            GenerateChangesCalls++;
            return Task.FromResult(new AiCodeChangeResult
            {
                Summary = _summary,
                ChangedFiles = Array.Empty<AiChangedFile>(),
                TestNotes = string.Empty
            });
        }

        public Task<AiCodeChangeResult> RegenerateFormattedChangesAsync(AgentContext agentContext, AiCodeChangeResult previousResult, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AiCodeChangeResult
            {
                Summary = _summary,
                ChangedFiles = Array.Empty<AiChangedFile>(),
                TestNotes = string.Empty
            });
        }

        public Task<AiCodeChangeResult> GenerateFixForTestFailureAsync(AgentContext agentContext, AiCodeChangeResult previousResult, BuildResult? buildResult, TestResult testResult, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AiCodeChangeResult
            {
                Summary = _summary,
                ChangedFiles = Array.Empty<AiChangedFile>(),
                TestNotes = string.Empty
            });
        }
    }
}
