namespace AiAgentChallenge.Infrastructure.Ai;

public sealed class AiOptions
{
    public string Provider { get; set; } = "OpenAI";

    public string Model { get; set; } = "gpt-4.1-mini";

    public string OpenAiApiBaseUrl { get; set; } = "https://api.openai.com";

    public string GeminiApiBaseUrl { get; set; } = "https://generativelanguage.googleapis.com";

    public int MaxContextFiles { get; init; } = 12;

    public int MaxFileBytes { get; init; } = 64 * 1024;

    public int MaxChangedFiles { get; init; } = 20;

    public int RequestTimeoutSeconds { get; init; } = 120;

    public int MaxTestFixAttempts { get; init; } = 1;

    public int MaxCriticalSignatures { get; init; } = 50;
}
