using AiAgentChallenge.Application.Tasks;

namespace AiAgentChallenge.Application.Abstractions;

public interface IGitClient
{
    Task<GitCloneResult> CloneAsync(
        string repositoryUrl,
        string baseBranch,
        string targetPath,
        CancellationToken cancellationToken = default);

    Task<GitCommandResult> CreateBranchAsync(
        string repoPath,
        string branchName,
        CancellationToken cancellationToken = default);

    Task<bool> HasChangesAsync(
        string repoPath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetChangedFilesAsync(
        string repoPath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetCommittedFilesAsync(
        string repoPath,
        CancellationToken cancellationToken = default);

    Task<string> GetDiffSummaryAsync(
        string repoPath,
        CancellationToken cancellationToken = default);

    Task<GitCommandResult> CommitAsync(
        string repoPath,
        string message,
        CancellationToken cancellationToken = default);

    Task<GitCommandResult> PushAsync(
        string repoPath,
        string branchName,
        CancellationToken cancellationToken = default);
}
