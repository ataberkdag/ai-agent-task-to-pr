using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Application.Abstractions;

public interface IPullRequestService
{
    Task<PullRequestResult> CreateOrGetPullRequestAsync(
        string repositoryUrl,
        string baseBranch,
        string headBranch,
        string title,
        string body,
        CancellationToken cancellationToken = default);
}
