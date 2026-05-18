namespace AiAgentChallenge.Infrastructure.Repositories;

public sealed class RepositoryPolicyOptions
{
    public string[] AllowedHosts { get; init; } = Array.Empty<string>();

    public string[] AllowedOwners { get; init; } = Array.Empty<string>();

    public string[] DisallowedHosts { get; init; } = Array.Empty<string>();

    public string[] DisallowedOwners { get; init; } = Array.Empty<string>();
}
