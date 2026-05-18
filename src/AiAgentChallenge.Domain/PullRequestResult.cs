namespace AiAgentChallenge.Domain;

public sealed class PullRequestResult
{
    public string PullRequestUrl { get; init; } = string.Empty;

    public int PullRequestNumber { get; init; }

    public PullRequestStatus Status { get; init; }
}
