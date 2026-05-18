using System.Net.Http.Json;
using System.Text.Json;
using AiAgentChallenge.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.Infrastructure.Ai;

public sealed class GeminiCodeAgent : IAiProviderCodeAgent
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly ILogger<GeminiCodeAgent> _logger;

    public GeminiCodeAgent(
        HttpClient httpClient,
        IOptions<AiOptions> options,
        ILogger<GeminiCodeAgent> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string ProviderName => "Gemini";

    public Task<AiCodeChangeResult> GenerateChangesAsync(
        AgentContext agentContext,
        CancellationToken cancellationToken = default)
    {
        return SendRequestAsync(agentContext, AiCodeAgentShared.BuildSystemPrompt(agentContext), cancellationToken);
    }

    public Task<AiCodeChangeResult> RegenerateFormattedChangesAsync(
        AgentContext agentContext,
        AiCodeChangeResult previousResult,
        CancellationToken cancellationToken = default)
    {
        return SendRequestAsync(
            AiCodeAgentShared.BuildFormattingRegenerationContext(agentContext, previousResult),
            AiCodeAgentShared.BuildFormattingRegenerationPrompt(),
            cancellationToken);
    }

    public Task<AiCodeChangeResult> GenerateFixForTestFailureAsync(
        AgentContext agentContext,
        AiCodeChangeResult previousResult,
        BuildResult? buildResult,
        TestResult testResult,
        CancellationToken cancellationToken = default)
    {
        return SendRequestAsync(
            AiCodeAgentShared.BuildFixContext(agentContext, previousResult, buildResult, testResult),
            AiCodeAgentShared.BuildFixSystemPrompt(agentContext, buildResult, testResult),
            cancellationToken);
    }

    private async Task<AiCodeChangeResult> SendRequestAsync(
        AgentContext agentContext,
        string systemPrompt,
        CancellationToken cancellationToken)
    {
        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("GEMINI_API_KEY environment variable is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.Model))
        {
            throw new InvalidOperationException("Ai:Model configuration is required.");
        }

        var baseUrl = string.IsNullOrWhiteSpace(_options.GeminiApiBaseUrl)
            ? "https://generativelanguage.googleapis.com"
            : _options.GeminiApiBaseUrl.TrimEnd('/');

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{baseUrl}/v1beta/models/{Uri.EscapeDataString(_options.Model)}:generateContent");

        request.Headers.Add("x-goog-api-key", apiKey);
        request.Content = JsonContent.Create(BuildRequestBody(agentContext, systemPrompt), options: JsonOptions);

        _logger.LogInformation("Requesting AI code changes with provider {Provider} and model {Model}", ProviderName, _options.Model);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Gemini request failed with status code {StatusCode} and reason {ReasonPhrase}",
                (int)response.StatusCode,
                response.ReasonPhrase);
            throw new InvalidOperationException(
                $"Gemini request failed with status {(int)response.StatusCode}: {response.ReasonPhrase}");
        }

        try
        {
            using var document = JsonDocument.Parse(responseContent);
            var root = document.RootElement;

            var messageContent = string.Concat(
                root.GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")
                    .EnumerateArray()
                    .Select(part => part.TryGetProperty("text", out var textElement)
                        ? textElement.GetString()
                        : null)
                    .Where(text => !string.IsNullOrWhiteSpace(text)));

            if (string.IsNullOrWhiteSpace(messageContent))
            {
                throw new InvalidOperationException("Gemini response did not contain candidate text content.");
            }

            var aiResult = AiCodeAgentShared.DeserializeAiResult(messageContent);
            var usage = root.TryGetProperty("usageMetadata", out var usageElement)
                ? new AiUsageInfo
                {
                    Model = _options.Model,
                    InputTokens = usageElement.TryGetProperty("promptTokenCount", out var promptTokens) ? promptTokens.GetInt32() : null,
                    OutputTokens = usageElement.TryGetProperty("candidatesTokenCount", out var candidateTokens) ? candidateTokens.GetInt32() : null,
                    TotalTokens = usageElement.TryGetProperty("totalTokenCount", out var totalTokens) ? totalTokens.GetInt32() : null
                }
                : new AiUsageInfo
                {
                    Model = _options.Model
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
            _logger.LogError(exception, "Gemini response was not valid JSON.");
            throw new InvalidOperationException("AI response was not valid JSON.", exception);
        }
    }

    private object BuildRequestBody(AgentContext agentContext, string systemPrompt)
    {
        return new
        {
            systemInstruction = new
            {
                parts = new[]
                {
                    new
                    {
                        text = systemPrompt
                    }
                }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[]
                    {
                        new
                        {
                            text = AiCodeAgentShared.BuildUserPrompt(agentContext)
                        }
                    }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseJsonSchema = AiCodeAgentShared.BuildResponseJsonSchema()
            }
        };
    }
}
