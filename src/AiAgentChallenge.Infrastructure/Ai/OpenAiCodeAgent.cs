using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AiAgentChallenge.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.Infrastructure.Ai;

public sealed class OpenAiCodeAgent : IAiProviderCodeAgent
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly ILogger<OpenAiCodeAgent> _logger;

    public OpenAiCodeAgent(
        HttpClient httpClient,
        IOptions<AiOptions> options,
        ILogger<OpenAiCodeAgent> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string ProviderName => "OpenAI";

    public async Task<AiCodeChangeResult> GenerateChangesAsync(
        AgentContext agentContext,
        CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync(agentContext, AiCodeAgentShared.BuildSystemPrompt(agentContext), cancellationToken);
    }

    public async Task<AiCodeChangeResult> RegenerateFormattedChangesAsync(
        AgentContext agentContext,
        AiCodeChangeResult previousResult,
        CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync(
            AiCodeAgentShared.BuildFormattingRegenerationContext(agentContext, previousResult),
            AiCodeAgentShared.BuildFormattingRegenerationPrompt(),
            cancellationToken);
    }

    public async Task<AiCodeChangeResult> GenerateFixForTestFailureAsync(
        AgentContext agentContext,
        AiCodeChangeResult previousResult,
        BuildResult? buildResult,
        TestResult testResult,
        CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync(
            AiCodeAgentShared.BuildFixContext(agentContext, previousResult, buildResult, testResult),
            AiCodeAgentShared.BuildFixSystemPrompt(agentContext, buildResult, testResult),
            cancellationToken);
    }

    private async Task<AiCodeChangeResult> SendRequestAsync(
        AgentContext agentContext,
        string systemPrompt,
        CancellationToken cancellationToken)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OPENAI_API_KEY environment variable is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.Model))
        {
            throw new InvalidOperationException("Ai:Model configuration is required.");
        }

        var baseUrl = string.IsNullOrWhiteSpace(_options.OpenAiApiBaseUrl)
            ? "https://api.openai.com"
            : _options.OpenAiApiBaseUrl.TrimEnd('/');

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(BuildRequestBody(agentContext, systemPrompt), options: JsonOptions);

        _logger.LogInformation("Requesting AI code changes with provider {Provider} and model {Model}", ProviderName, _options.Model);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "OpenAI request failed with status code {StatusCode} and reason {ReasonPhrase}",
                (int)response.StatusCode,
                response.ReasonPhrase);
            throw new InvalidOperationException(
                $"OpenAI request failed with status {(int)response.StatusCode}: {response.ReasonPhrase}");
        }

        try
        {
            using var document = JsonDocument.Parse(responseContent);
            var root = document.RootElement;

            var messageContent = root
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(messageContent))
            {
                throw new InvalidOperationException("OpenAI response did not contain message content.");
            }

            var aiResult = AiCodeAgentShared.DeserializeAiResult(messageContent);

            var usage = root.TryGetProperty("usage", out var usageElement)
                ? new AiUsageInfo
                {
                    Model = root.TryGetProperty("model", out var modelElement)
                        ? modelElement.GetString() ?? _options.Model
                        : _options.Model,
                    InputTokens = usageElement.TryGetProperty("prompt_tokens", out var promptTokens) ? promptTokens.GetInt32() : null,
                    OutputTokens = usageElement.TryGetProperty("completion_tokens", out var completionTokens) ? completionTokens.GetInt32() : null,
                    TotalTokens = usageElement.TryGetProperty("total_tokens", out var totalTokens) ? totalTokens.GetInt32() : null
                }
                : new AiUsageInfo
                {
                    Model = root.TryGetProperty("model", out var modelValue)
                        ? modelValue.GetString() ?? _options.Model
                        : _options.Model
                };

            return new AiCodeChangeResult
            {
                Summary = aiResult.Summary,
                ChangedFiles = aiResult.ChangedFiles,
                TestNotes = aiResult.TestNotes,
                Usage = usage,
                Warnings = aiResult.Warnings
            };
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "OpenAI response was not valid JSON.");
            throw new InvalidOperationException("AI response was not valid JSON.", exception);
        }
    }

    private object BuildRequestBody(AgentContext agentContext, string systemPrompt)
    {
        return new
        {
            model = _options.Model,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = systemPrompt
                },
                new
                {
                    role = "user",
                    content = AiCodeAgentShared.BuildUserPrompt(agentContext)
                }
            },
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "ai_code_change_result",
                    strict = true,
                    schema = AiCodeAgentShared.BuildResponseJsonSchema()
                }
            }
        };
    }
}
