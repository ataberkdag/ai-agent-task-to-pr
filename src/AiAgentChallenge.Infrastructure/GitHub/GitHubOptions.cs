namespace AiAgentChallenge.Infrastructure.GitHub;

public sealed class GitHubOptions
{
    public string TokenEnvironmentVariable { get; init; } = "GITHUB_TOKEN";

    public string ApiBaseUrl { get; init; } = "https://api.github.com";
}
