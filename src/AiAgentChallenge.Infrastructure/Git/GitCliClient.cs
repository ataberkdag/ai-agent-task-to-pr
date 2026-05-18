using System.Text;
using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Application.Tasks;
using AiAgentChallenge.Infrastructure.GitHub;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.Infrastructure.Git;

public sealed class GitCliClient : IGitClient
{
    private readonly IProcessRunner _processRunner;
    private readonly GitOptions _gitOptions;
    private readonly GitHubOptions _gitHubOptions;
    private readonly ILogger<GitCliClient> _logger;

    public GitCliClient(
        IProcessRunner processRunner,
        IOptions<GitOptions> gitOptions,
        IOptions<GitHubOptions> gitHubOptions,
        ILogger<GitCliClient> logger)
    {
        _processRunner = processRunner;
        _gitOptions = gitOptions.Value;
        _gitHubOptions = gitHubOptions.Value;
        _logger = logger;
    }

    public async Task<GitCloneResult> CloneAsync(
        string repositoryUrl,
        string baseBranch,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathRooted(targetPath))
        {
            throw new InvalidOperationException("Git clone target path must be an absolute path.");
        }

        var fullTargetPath = Path.GetFullPath(targetPath);
        var workingDirectory = Path.GetDirectoryName(fullTargetPath) ?? Directory.GetCurrentDirectory();

        _logger.LogInformation(
            "Cloning repository from {RepositoryUrl} into {TargetPath} on branch {BaseBranch}",
            SanitizeRepositoryUrl(repositoryUrl),
            fullTargetPath,
            baseBranch);

        var arguments = BuildCloneArguments(repositoryUrl, baseBranch, fullTargetPath, out var cloneAuthError);
        if (cloneAuthError is not null)
        {
            _logger.LogError(
                "Git clone could not start for repository {RepositoryUrl}: {CloneAuthError}",
                SanitizeRepositoryUrl(repositoryUrl),
                cloneAuthError);

            return GitCloneResult.Failure(
                GitCloneErrorCode.PermissionDenied,
                cloneAuthError,
                -1,
                string.Empty,
                string.Empty);
        }

        var request = new ProcessExecutionRequest
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            Arguments = arguments,
            Timeout = TimeSpan.FromSeconds(Math.Max(1, _gitOptions.CloneTimeoutSeconds))
        };

        var result = await _processRunner.RunAsync(request, cancellationToken);
        if (result.TimedOut)
        {
            _logger.LogError("Git clone timed out for repository {RepositoryUrl}", SanitizeRepositoryUrl(repositoryUrl));
            return GitCloneResult.Failure(
                GitCloneErrorCode.Timeout,
                "Git clone operation timed out.",
                result.ExitCode,
                result.StandardOutput,
                result.StandardError);
        }

        if (result.ExitCode == 0)
        {
            _logger.LogInformation("Repository clone completed successfully for {RepositoryUrl}", SanitizeRepositoryUrl(repositoryUrl));
            return GitCloneResult.Success(result.ExitCode, result.StandardOutput, result.StandardError);
        }

        var combinedOutput = $"{result.StandardError}\n{result.StandardOutput}".Trim();
        var normalized = combinedOutput.ToLowerInvariant();

        if (normalized.Contains("remote branch") && normalized.Contains("not found"))
        {
            _logger.LogError("Git clone failed because branch {BaseBranch} was not found for {RepositoryUrl}", baseBranch, SanitizeRepositoryUrl(repositoryUrl));
            return GitCloneResult.Failure(
                GitCloneErrorCode.BranchNotFound,
                "The requested base branch was not found in the remote repository.",
                result.ExitCode,
                result.StandardOutput,
                result.StandardError);
        }

        if (normalized.Contains("authentication failed") ||
            normalized.Contains("permission denied") ||
            normalized.Contains("repository not found"))
        {
            _logger.LogError("Git clone failed due to authentication or permission issues for {RepositoryUrl}", SanitizeRepositoryUrl(repositoryUrl));
            return GitCloneResult.Failure(
                GitCloneErrorCode.PermissionDenied,
                "The repository could not be accessed. Check permissions and repository visibility.",
                result.ExitCode,
                result.StandardOutput,
                result.StandardError);
        }

        _logger.LogError("Git clone failed for repository {RepositoryUrl}", SanitizeRepositoryUrl(repositoryUrl));
        return GitCloneResult.Failure(
            GitCloneErrorCode.CloneFailed,
            "Git clone failed. Review the command output for details.",
            result.ExitCode,
            result.StandardOutput,
            result.StandardError);
    }

    private string[] BuildCloneArguments(
        string repositoryUrl,
        string baseBranch,
        string fullTargetPath,
        out string? cloneAuthError)
    {
        var arguments = BuildAuthenticatedGitArguments(
            repositoryUrl,
            "The GitHub repository could not be accessed because the {0} environment variable is missing or empty.",
            out cloneAuthError);

        arguments.AddRange(
        [
            "clone",
            "--branch",
            baseBranch,
            "--single-branch",
            repositoryUrl,
            fullTargetPath
        ]);

        return arguments.ToArray();
    }

    public async Task<GitCommandResult> CreateBranchAsync(
        string repoPath,
        string branchName,
        CancellationToken cancellationToken = default)
    {
        var result = await RunGitAsync(
            repoPath,
            new[] { "checkout", "-b", branchName },
            TimeSpan.FromSeconds(Math.Max(1, _gitOptions.PushTimeoutSeconds)),
            cancellationToken);

        if (result.ExitCode == 0)
        {
            _logger.LogInformation("Created branch {BranchName} in repository {RepositoryPath}", branchName, repoPath);
        }
        else
        {
            _logger.LogError("Failed to create branch {BranchName} in repository {RepositoryPath}", branchName, repoPath);
        }

        return result.ExitCode == 0
            ? GitCommandResult.Success("Branch created successfully.", result.ExitCode, result.StandardOutput, result.StandardError)
            : GitCommandResult.Failure("Failed to create branch.", result.ExitCode, result.StandardOutput, result.StandardError);
    }

    public async Task<bool> HasChangesAsync(
        string repoPath,
        CancellationToken cancellationToken = default)
    {
        var result = await RunGitAsync(
            repoPath,
            new[] { "status", "--porcelain" },
            TimeSpan.FromSeconds(Math.Max(1, _gitOptions.PushTimeoutSeconds)),
            cancellationToken);

        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput);
    }

    public async Task<IReadOnlyList<string>> GetChangedFilesAsync(
        string repoPath,
        CancellationToken cancellationToken = default)
    {
        return await ReadFileListAsync(
            repoPath,
            new[] { "diff", "--name-only" },
            cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetCommittedFilesAsync(
        string repoPath,
        CancellationToken cancellationToken = default)
    {
        return await ReadFileListAsync(
            repoPath,
            new[] { "diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD" },
            cancellationToken);
    }

    public async Task<string> GetDiffSummaryAsync(
        string repoPath,
        CancellationToken cancellationToken = default)
    {
        var result = await RunGitAsync(
            repoPath,
            new[] { "diff", "--stat" },
            TimeSpan.FromSeconds(Math.Max(1, _gitOptions.PushTimeoutSeconds)),
            cancellationToken);

        return result.ExitCode == 0
            ? result.StandardOutput.Trim()
            : string.Empty;
    }

    public async Task<GitCommandResult> CommitAsync(
        string repoPath,
        string message,
        CancellationToken cancellationToken = default)
    {
        var addResult = await RunGitAsync(
            repoPath,
            new[] { "add", "." },
            TimeSpan.FromSeconds(Math.Max(1, _gitOptions.PushTimeoutSeconds)),
            cancellationToken);

        if (addResult.ExitCode != 0)
        {
            _logger.LogError("Failed to stage changes in repository {RepositoryPath}", repoPath);
            return GitCommandResult.Failure("Failed to stage changes.", addResult.ExitCode, addResult.StandardOutput, addResult.StandardError);
        }

        var commitResult = await RunGitAsync(
            repoPath,
            new[] { "commit", "-m", message },
            TimeSpan.FromSeconds(Math.Max(1, _gitOptions.PushTimeoutSeconds)),
            cancellationToken);

        if (commitResult.ExitCode == 0)
        {
            _logger.LogInformation("Created commit in repository {RepositoryPath} with message {CommitMessage}", repoPath, message);
        }
        else
        {
            _logger.LogError("Failed to create commit in repository {RepositoryPath}", repoPath);
        }

        return commitResult.ExitCode == 0
            ? GitCommandResult.Success("Changes committed successfully.", commitResult.ExitCode, commitResult.StandardOutput, commitResult.StandardError)
            : GitCommandResult.Failure("Failed to commit changes.", commitResult.ExitCode, commitResult.StandardOutput, commitResult.StandardError);
    }

    public async Task<GitCommandResult> PushAsync(
        string repoPath,
        string branchName,
        CancellationToken cancellationToken = default)
    {
        var remoteUrl = await GetRemoteUrlAsync(repoPath, "origin", cancellationToken);
        var arguments = BuildAuthenticatedGitArguments(
            remoteUrl,
            "The GitHub branch could not be pushed because the {0} environment variable is missing or empty.",
            out var pushAuthError);

        if (pushAuthError is not null)
        {
            _logger.LogError("Git push could not start for branch {BranchName}: {PushAuthError}", branchName, pushAuthError);
            return GitCommandResult.Failure(pushAuthError, -1, string.Empty, string.Empty);
        }

        arguments.AddRange(
        [
            "push",
            "--set-upstream",
            "origin",
            branchName
        ]);

        var result = await RunGitAsync(
            repoPath,
            arguments,
            TimeSpan.FromSeconds(Math.Max(1, _gitOptions.PushTimeoutSeconds)),
            cancellationToken);

        if (result.ExitCode == 0)
        {
            _logger.LogInformation("Pushed branch {BranchName} from repository {RepositoryPath}", branchName, repoPath);
            return GitCommandResult.Success("Branch pushed successfully.", result.ExitCode, result.StandardOutput, result.StandardError);
        }

        var normalized = $"{result.StandardError}\n{result.StandardOutput}".ToLowerInvariant();
        if (normalized.Contains("authentication") ||
            normalized.Contains("permission denied") ||
            normalized.Contains("403") ||
            normalized.Contains("repository not found") ||
            normalized.Contains("could not read username") ||
            normalized.Contains("could not authenticate"))
        {
            _logger.LogError("Git push failed due to authentication or permission issues for branch {BranchName}", branchName);
            return GitCommandResult.Failure("Git push failed due to authentication or permission issues.", result.ExitCode, result.StandardOutput, result.StandardError);
        }

        _logger.LogError("Failed to push branch {BranchName} from repository {RepositoryPath}", branchName, repoPath);
        return GitCommandResult.Failure("Failed to push branch to remote.", result.ExitCode, result.StandardOutput, result.StandardError);
    }

    private List<string> BuildAuthenticatedGitArguments(
        string? repositoryUrl,
        string missingTokenMessageFormat,
        out string? authError)
    {
        authError = null;
        var arguments = new List<string>();

        if (!IsGitHubHttpsRepository(repositoryUrl))
        {
            return arguments;
        }

        var tokenVariableName = string.IsNullOrWhiteSpace(_gitHubOptions.TokenEnvironmentVariable)
            ? "GITHUB_TOKEN"
            : _gitHubOptions.TokenEnvironmentVariable;
        var token = Environment.GetEnvironmentVariable(tokenVariableName);

        if (string.IsNullOrWhiteSpace(token))
        {
            authError = string.Format(missingTokenMessageFormat, tokenVariableName);
            return arguments;
        }

        arguments.Add("-c");
        arguments.Add($"http.extraHeader=AUTHORIZATION: basic {BuildGitHubBasicAuthValue(token)}");
        return arguments;
    }

    private async Task<string?> GetRemoteUrlAsync(
        string repoPath,
        string remoteName,
        CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(
            repoPath,
            new[] { "remote", "get-url", remoteName },
            TimeSpan.FromSeconds(Math.Max(1, _gitOptions.PushTimeoutSeconds)),
            cancellationToken);

        return result.ExitCode == 0
            ? result.StandardOutput.Trim()
            : null;
    }

    private async Task<IReadOnlyList<string>> ReadFileListAsync(
        string repoPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(
            repoPath,
            arguments,
            TimeSpan.FromSeconds(Math.Max(1, _gitOptions.PushTimeoutSeconds)),
            cancellationToken);

        if (result.ExitCode != 0)
        {
            return Array.Empty<string>();
        }

        return result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    private Task<ProcessExecutionResult> RunGitAsync(
        string repoPath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return _processRunner.RunAsync(
            new ProcessExecutionRequest
            {
                FileName = "git",
                WorkingDirectory = repoPath,
                Arguments = arguments,
                Timeout = timeout
            },
            cancellationToken);
    }

    private static string SanitizeRepositoryUrl(string repositoryUrl)
    {
        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri))
        {
            return "invalid-url";
        }

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty
        };

        return builder.Uri.ToString();
    }

    private static bool IsGitHubHttpsRepository(string? repositoryUrl)
    {
        return Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri) &&
               uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildGitHubBasicAuthValue(string token)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{token}"));
    }
}
