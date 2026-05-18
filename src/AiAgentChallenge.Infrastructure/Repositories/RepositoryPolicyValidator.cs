using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Application.Tasks;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.Infrastructure.Repositories;

public sealed class RepositoryPolicyValidator : IRepositoryPolicy
{
    private readonly RepositoryPolicyOptions _options;

    public RepositoryPolicyValidator(IOptions<RepositoryPolicyOptions> options)
    {
        _options = options.Value;
    }

    public RepositoryPolicyResult Validate(string repositoryUrl)
    {
        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri))
        {
            return RepositoryPolicyResult.Failure(
                RepositoryPolicyErrorCode.InvalidUrl,
                "Repository URL must be a valid absolute URL.");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return RepositoryPolicyResult.Failure(
                RepositoryPolicyErrorCode.InvalidScheme,
                "Repository URL must use HTTPS.");
        }

        if (_options.DisallowedHosts.Any(host => string.Equals(host, uri.Host, StringComparison.OrdinalIgnoreCase)))
        {
            return RepositoryPolicyResult.Failure(
                RepositoryPolicyErrorCode.DisallowedHost,
                $"Repository host '{uri.Host}' is denied by policy.");
        }

        if (_options.AllowedHosts.Length > 0 &&
            !_options.AllowedHosts.Any(host => string.Equals(host, uri.Host, StringComparison.OrdinalIgnoreCase)))
        {
            return RepositoryPolicyResult.Failure(
                RepositoryPolicyErrorCode.DisallowedHost,
                $"Repository host '{uri.Host}' is not allowed.");
        }

        var owner = ExtractOwner(uri);
        if (string.IsNullOrWhiteSpace(owner))
        {
            return RepositoryPolicyResult.Failure(
                RepositoryPolicyErrorCode.InvalidUrl,
                "Repository URL must include an owner segment.");
        }

        if (_options.DisallowedOwners.Any(disallowedOwner => string.Equals(disallowedOwner, owner, StringComparison.OrdinalIgnoreCase)))
        {
            return RepositoryPolicyResult.Failure(
                RepositoryPolicyErrorCode.DisallowedOwner,
                $"Repository owner '{owner}' is denied by policy.");
        }

        if (_options.AllowedOwners.Length > 0 &&
            !_options.AllowedOwners.Any(allowedOwner => string.Equals(allowedOwner, owner, StringComparison.OrdinalIgnoreCase)))
        {
            return RepositoryPolicyResult.Failure(
                RepositoryPolicyErrorCode.DisallowedOwner,
                $"Repository owner '{owner}' is not allowed.");
        }

        return RepositoryPolicyResult.Success(owner);
    }

    private static string ExtractOwner(Uri uri)
    {
        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2 ? segments[0] : string.Empty;
    }
}
