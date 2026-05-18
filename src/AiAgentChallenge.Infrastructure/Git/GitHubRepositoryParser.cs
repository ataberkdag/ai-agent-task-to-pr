using AiAgentChallenge.Application.Abstractions;

namespace AiAgentChallenge.Infrastructure.Git;

public sealed class GitHubRepositoryParser : IGitHubRepositoryParser
{
    public (string Owner, string RepositoryName) Parse(string repositoryUrl)
    {
        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Repository URL must be a valid GitHub HTTPS URL.");
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            throw new InvalidOperationException("Repository URL must include owner and repository name.");
        }

        var owner = segments[0];
        var repo = segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? segments[1][..^4]
            : segments[1];

        return (owner, repo);
    }
}
