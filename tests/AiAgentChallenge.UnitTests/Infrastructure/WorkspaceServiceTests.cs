using AiAgentChallenge.Infrastructure.Paths;
using AiAgentChallenge.Infrastructure.Workspace;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class WorkspaceServiceTests
{
    [Fact]
    public async Task CreateAsync_CreatesPathSafeWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new WorkspaceService(Options.Create(new WorkspaceOptions
        {
            WorkspaceRoot = root
        }));

        try
        {
            var workspace = await service.CreateAsync("TASK:123/email validation");

            Assert.True(Path.IsPathRooted(workspace.WorkspacePath));
            Assert.True(Path.IsPathRooted(workspace.RepositoryPath));
            Assert.Contains(Path.Combine(root, "TASK-123-email-validation"), workspace.WorkspacePath);
            Assert.EndsWith(Path.Combine("source"), workspace.RepositoryPath);
            Assert.StartsWith(workspace.WorkspacePath, workspace.RepositoryPath, StringComparison.OrdinalIgnoreCase);
            Assert.True(Directory.Exists(workspace.RepositoryPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CreateAsync_CreatesDifferentWorkspaceForSameTask()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new WorkspaceService(Options.Create(new WorkspaceOptions
        {
            WorkspaceRoot = root
        }));

        try
        {
            var first = await service.CreateAsync("TASK-123");
            var second = await service.CreateAsync("TASK-123");

            Assert.NotEqual(first.WorkspacePath, second.WorkspacePath);
            Assert.NotEqual(first.RepositoryPath, second.RepositoryPath);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CreateAsync_NormalizesRelativeWorkspaceRoot_AndDoesNotDuplicateSegments()
    {
        var relativeRoot = Path.Combine("workspaces-test", Guid.NewGuid().ToString("N"));
        var service = new WorkspaceService(Options.Create(new WorkspaceOptions
        {
            WorkspaceRoot = relativeRoot
        }));

        var applicationBaseDirectory = ApplicationPathResolver.GetApplicationBaseDirectory();
        var normalizedRoot = Path.GetFullPath(Path.Combine(applicationBaseDirectory, relativeRoot));

        try
        {
            var workspace = await service.CreateAsync("TASK-1");

            Assert.True(Path.IsPathRooted(workspace.WorkspacePath));
            Assert.True(Path.IsPathRooted(workspace.RepositoryPath));
            Assert.StartsWith(normalizedRoot, workspace.WorkspacePath, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(workspace.WorkspacePath, workspace.RepositoryPath, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, CountOccurrences(NormalizeSeparators(workspace.RepositoryPath), "/source"));
            Assert.Equal(1, CountOccurrences(NormalizeSeparators(workspace.RepositoryPath), "/TASK-1/"));
            Assert.DoesNotContain("/workspaces/TASK-1/workspaces/TASK-1/", NormalizeSeparators(workspace.RepositoryPath), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(normalizedRoot))
            {
                Directory.Delete(normalizedRoot, recursive: true);
            }

            var rootParent = Path.Combine(applicationBaseDirectory, "workspaces-test");
            if (Directory.Exists(rootParent) &&
                !Directory.EnumerateFileSystemEntries(rootParent).Any())
            {
                Directory.Delete(rootParent);
            }
        }
    }

    private static string NormalizeSeparators(string value)
    {
        return value.Replace('\\', '/');
    }

    private static int CountOccurrences(string source, string token)
    {
        var count = 0;
        var index = 0;

        while ((index = source.IndexOf(token, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }
}
