using System.Text;
using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Domain;
using AiAgentChallenge.Infrastructure.Paths;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.Infrastructure.Workspace;

public sealed class WorkspaceService : IWorkspaceService
{
    private readonly string _workspaceRoot;

    public WorkspaceService(IOptions<WorkspaceOptions> options)
    {
        _workspaceRoot = NormalizeWorkspaceRoot(options.Value.WorkspaceRoot);
    }

    public Task<WorkspaceInfo> CreateAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var safeTaskId = MakePathSafe(taskId);
        var runId = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}".Substring(0, 23);
        var workspacePath = Path.GetFullPath(Path.Combine(_workspaceRoot, safeTaskId, runId));
        var repositoryPath = Path.GetFullPath(Path.Combine(workspacePath, "source"));

        Directory.CreateDirectory(repositoryPath);

        return Task.FromResult(new WorkspaceInfo
        {
            SafeTaskId = safeTaskId,
            RunId = runId,
            WorkspacePath = workspacePath,
            RepositoryPath = repositoryPath
        });
    }

    internal static string NormalizeWorkspaceRoot(string workspaceRoot)
    {
        return ApplicationPathResolver.ResolveAgainstApplicationBase(workspaceRoot, "workspaces");
    }

    internal static string GetRepositoryRoot()
    {
        return ApplicationPathResolver.GetRepositoryRoot();
    }

    internal static string MakePathSafe(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "task";
        }

        var builder = new StringBuilder(value.Length);

        foreach (var character in value.Trim())
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '-');
        }

        var sanitized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "task" : sanitized;
    }
}
