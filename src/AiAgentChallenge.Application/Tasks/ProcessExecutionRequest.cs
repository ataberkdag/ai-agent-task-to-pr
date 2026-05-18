namespace AiAgentChallenge.Application.Tasks;

public sealed class ProcessExecutionRequest
{
    public string FileName { get; init; } = string.Empty;

    public string WorkingDirectory { get; init; } = string.Empty;

    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    public TimeSpan? Timeout { get; init; }

    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } =
        new Dictionary<string, string>();
}
