using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Application.Tasks;
using AiAgentChallenge.Infrastructure.Git;
using AiAgentChallenge.Infrastructure.GitHub;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class GitCliClientTests
{
    [Fact]
    public async Task CloneAsync_BuildsExpectedCloneArguments()
    {
        var fakeRunner = new FakeProcessRunner(new ProcessExecutionResult
        {
            ExitCode = 0,
            StandardOutput = "ok"
        });
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", "secret-token");

        try
        {
            var client = CreateClient(fakeRunner);

            var result = await client.CloneAsync(
                "https://github.com/example-company/user-service",
                "develop",
                @"C:\temp\repo");

            Assert.True(result.IsSuccess);
            Assert.NotNull(fakeRunner.LastRequest);
            Assert.Equal("git", fakeRunner.LastRequest!.FileName);
            Assert.Equal("-c", fakeRunner.LastRequest.Arguments[0]);
            Assert.StartsWith("http.extraHeader=AUTHORIZATION: basic ", fakeRunner.LastRequest.Arguments[1]);
            Assert.DoesNotContain("secret-token", fakeRunner.LastRequest.Arguments[1], StringComparison.Ordinal);
            Assert.Equal(
                new[]
                {
                    "clone",
                    "--branch",
                    "develop",
                    "--single-branch",
                    "https://github.com/example-company/user-service",
                    @"C:\temp\repo"
                },
                fakeRunner.LastRequest.Arguments.Skip(2).ToArray());
            Assert.Equal(@"C:\temp", fakeRunner.LastRequest.WorkingDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);
        }
    }

    [Fact]
    public async Task CloneAsync_ReturnsBranchNotFoundError()
    {
        var fakeRunner = new FakeProcessRunner(new ProcessExecutionResult
        {
            ExitCode = 1,
            StandardError = "fatal: Remote branch develop not found in upstream origin"
        });
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", "secret-token");

        try
        {
            var client = CreateClient(fakeRunner);

            var result = await client.CloneAsync(
                "https://github.com/example-company/user-service",
                "develop",
                @"C:\temp\repo");

            Assert.False(result.IsSuccess);
            Assert.Equal(GitCloneErrorCode.BranchNotFound, result.ErrorCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);
        }
    }

    [Fact]
    public async Task CloneAsync_DoesNotRecombineTargetPath()
    {
        var fakeRunner = new FakeProcessRunner(new ProcessExecutionResult
        {
            ExitCode = 0,
            StandardOutput = "ok"
        });
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", "secret-token");

        try
        {
            var client = CreateClient(fakeRunner);
            var targetPath = Path.GetFullPath(Path.Combine("workspaces", "TASK-1", "20260515153053-cd1a78c", "source"));

            var result = await client.CloneAsync(
                "https://github.com/example-company/user-service",
                "main",
                targetPath);

            Assert.True(result.IsSuccess);
            Assert.NotNull(fakeRunner.LastRequest);
            Assert.Equal(targetPath, fakeRunner.LastRequest!.Arguments[^1]);
            Assert.Equal(Path.GetDirectoryName(targetPath), fakeRunner.LastRequest.WorkingDirectory);
            Assert.DoesNotContain(
                $"{NormalizeSeparators(targetPath)}/{NormalizeSeparators(targetPath)}",
                NormalizeSeparators(string.Join(' ', fakeRunner.LastRequest.Arguments)),
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "/workspaces/TASK-1/20260515153053-cd1a78c/workspaces/TASK-1/",
                NormalizeSeparators(string.Join(' ', fakeRunner.LastRequest.Arguments)),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);
        }
    }

    [Fact]
    public async Task CloneAsync_ReturnsPermissionDenied_WhenGitHubTokenIsMissing()
    {
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);
        var fakeRunner = new FakeProcessRunner(new ProcessExecutionResult
        {
            ExitCode = 0,
            StandardOutput = "ok"
        });
        var client = CreateClient(fakeRunner);

        var result = await client.CloneAsync(
            "https://github.com/example-company/user-service",
            "main",
            @"C:\temp\repo");

        Assert.False(result.IsSuccess);
        Assert.Equal(GitCloneErrorCode.PermissionDenied, result.ErrorCode);
        Assert.Contains("GITHUB_TOKEN", result.Message, StringComparison.Ordinal);
        Assert.Null(fakeRunner.LastRequest);
    }

    [Fact]
    public async Task CloneAsync_UsesAnonymousClone_ForNonGitHubHosts()
    {
        var fakeRunner = new FakeProcessRunner(new ProcessExecutionResult
        {
            ExitCode = 0,
            StandardOutput = "ok"
        });
        var client = CreateClient(fakeRunner);

        var result = await client.CloneAsync(
            "https://gitlab.com/example-company/user-service",
            "main",
            @"C:\temp\repo");

        Assert.True(result.IsSuccess);
        Assert.NotNull(fakeRunner.LastRequest);
        Assert.Equal(
            new[]
            {
                "clone",
                "--branch",
                "main",
                "--single-branch",
                "https://gitlab.com/example-company/user-service",
                @"C:\temp\repo"
            },
            fakeRunner.LastRequest!.Arguments);
    }

    [Fact]
    public async Task CreateBranchAsync_BuildsExpectedArguments()
    {
        var fakeRunner = new FakeProcessRunner(new ProcessExecutionResult
        {
            ExitCode = 0,
            StandardOutput = "ok"
        });

        var client = CreateClient(fakeRunner);

        var result = await client.CreateBranchAsync(@"C:\temp\repo", "ai-agent/TASK-123-add-email-validation");

        Assert.True(result.IsSuccess);
        Assert.NotNull(fakeRunner.LastRequest);
        Assert.Equal("git", fakeRunner.LastRequest!.FileName);
        Assert.Equal(
            new[]
            {
                "checkout",
                "-b",
                "ai-agent/TASK-123-add-email-validation"
            },
            fakeRunner.LastRequest.Arguments);
    }

    [Fact]
    public async Task CommitAsync_BuildsExpectedAddAndCommitArguments()
    {
        var fakeRunner = new FakeProcessRunner(
            new ProcessExecutionResult
            {
                ExitCode = 0,
                StandardOutput = "added"
            },
            new ProcessExecutionResult
            {
                ExitCode = 0,
                StandardOutput = "committed"
            });

        var client = CreateClient(fakeRunner);

        var result = await client.CommitAsync(@"C:\temp\repo", "TASK-123 Add email validation");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, fakeRunner.Requests.Count);
        Assert.Equal(new[] { "add", "." }, fakeRunner.Requests[0].Arguments);
        Assert.Equal(
            new[] { "commit", "-m", "TASK-123 Add email validation" },
            fakeRunner.Requests[1].Arguments);
    }

    [Fact]
    public async Task PushAsync_BuildsExpectedArguments()
    {
        var fakeRunner = new FakeProcessRunner(
            new ProcessExecutionResult
            {
                ExitCode = 1,
                StandardOutput = "origin-url",
                StandardError = "remote lookup failed"
            },
            new ProcessExecutionResult
            {
                ExitCode = 0,
                StandardOutput = "pushed"
            });

        var client = CreateClient(fakeRunner);

        var result = await client.PushAsync(@"C:\temp\repo", "ai-agent/TASK-123-add-email-validation");

        Assert.True(result.IsSuccess);
        Assert.NotNull(fakeRunner.LastRequest);
        Assert.Equal(2, fakeRunner.Requests.Count);
        Assert.Equal(new[] { "remote", "get-url", "origin" }, fakeRunner.Requests[0].Arguments);
        Assert.Equal(
            new[] { "push", "--set-upstream", "origin", "ai-agent/TASK-123-add-email-validation" },
            fakeRunner.LastRequest!.Arguments);
    }

    [Fact]
    public async Task PushAsync_UsesGitHubTokenHeader_ForGitHubHttpsOrigin()
    {
        var fakeRunner = new FakeProcessRunner(
            new ProcessExecutionResult
            {
                ExitCode = 0,
                StandardOutput = "https://github.com/example-company/user-service"
            },
            new ProcessExecutionResult
            {
                ExitCode = 0,
                StandardOutput = "pushed"
            });
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", "secret-token");

        try
        {
            var client = CreateClient(fakeRunner);

            var result = await client.PushAsync(@"C:\temp\repo", "ai-agent/TASK-123-add-email-validation");

            Assert.True(result.IsSuccess);
            Assert.Equal(2, fakeRunner.Requests.Count);
            Assert.Equal(new[] { "remote", "get-url", "origin" }, fakeRunner.Requests[0].Arguments);
            Assert.Equal("-c", fakeRunner.Requests[1].Arguments[0]);
            Assert.StartsWith("http.extraHeader=AUTHORIZATION: basic ", fakeRunner.Requests[1].Arguments[1]);
            Assert.DoesNotContain("secret-token", fakeRunner.Requests[1].Arguments[1], StringComparison.Ordinal);
            Assert.Equal(
                new[] { "push", "--set-upstream", "origin", "ai-agent/TASK-123-add-email-validation" },
                fakeRunner.Requests[1].Arguments.Skip(2).ToArray());
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);
        }
    }

    [Fact]
    public async Task PushAsync_ReturnsFailure_WhenGitHubTokenIsMissing()
    {
        var fakeRunner = new FakeProcessRunner(new ProcessExecutionResult
        {
            ExitCode = 0,
            StandardOutput = "https://github.com/example-company/user-service"
        });
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);

        var client = CreateClient(fakeRunner);

        var result = await client.PushAsync(@"C:\temp\repo", "ai-agent/TASK-123-add-email-validation");

        Assert.False(result.IsSuccess);
        Assert.Contains("GITHUB_TOKEN", result.Message, StringComparison.Ordinal);
        Assert.Single(fakeRunner.Requests);
        Assert.Equal(new[] { "remote", "get-url", "origin" }, fakeRunner.Requests[0].Arguments);
    }

    [Fact]
    public async Task PushAsync_DoesNotUseAuthHeader_ForSshOrigin()
    {
        var fakeRunner = new FakeProcessRunner(
            new ProcessExecutionResult
            {
                ExitCode = 0,
                StandardOutput = "git@github.com:example-company/user-service.git"
            },
            new ProcessExecutionResult
            {
                ExitCode = 0,
                StandardOutput = "pushed"
            });
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", "secret-token");

        try
        {
            var client = CreateClient(fakeRunner);

            var result = await client.PushAsync(@"C:\temp\repo", "ai-agent/TASK-123-add-email-validation");

            Assert.True(result.IsSuccess);
            Assert.Equal(
                new[] { "push", "--set-upstream", "origin", "ai-agent/TASK-123-add-email-validation" },
                fakeRunner.Requests[1].Arguments);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);
        }
    }

    [Fact]
    public async Task GetDiffSummaryAsync_BuildsExpectedArguments()
    {
        var fakeRunner = new FakeProcessRunner(new ProcessExecutionResult
        {
            ExitCode = 0,
            StandardOutput = " src/File.cs | 4 ++--"
        });

        var client = CreateClient(fakeRunner);

        var result = await client.GetDiffSummaryAsync(@"C:\temp\repo");

        Assert.Equal("src/File.cs | 4 ++--", result);
        Assert.NotNull(fakeRunner.LastRequest);
        Assert.Equal(new[] { "diff", "--stat" }, fakeRunner.LastRequest!.Arguments);
    }

    [Fact]
    public async Task GetCommittedFilesAsync_BuildsExpectedArguments()
    {
        var fakeRunner = new FakeProcessRunner(new ProcessExecutionResult
        {
            ExitCode = 0,
            StandardOutput = "src/File.cs\r\ntests/FileTests.cs\r\n"
        });

        var client = CreateClient(fakeRunner);

        var result = await client.GetCommittedFilesAsync(@"C:\temp\repo");

        Assert.Equal(new[] { "src/File.cs", "tests/FileTests.cs" }, result);
        Assert.NotNull(fakeRunner.LastRequest);
        Assert.Equal(
            new[] { "diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD" },
            fakeRunner.LastRequest!.Arguments);
    }

    private static GitCliClient CreateClient(FakeProcessRunner fakeRunner)
    {
        return new GitCliClient(
            fakeRunner,
            Options.Create(new GitOptions { CloneTimeoutSeconds = 120, PushTimeoutSeconds = 120 }),
            Options.Create(new GitHubOptions()),
            NullLogger<GitCliClient>.Instance);
    }

    private static string NormalizeSeparators(string value)
    {
        return value.Replace('\\', '/');
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly Queue<ProcessExecutionResult> _results;

        public FakeProcessRunner(params ProcessExecutionResult[] results)
        {
            _results = new Queue<ProcessExecutionResult>(results);
        }

        public ProcessExecutionRequest? LastRequest { get; private set; }

        public List<ProcessExecutionRequest> Requests { get; } = new();

        public Task<ProcessExecutionResult> RunAsync(
            ProcessExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            Requests.Add(request);
            return Task.FromResult(_results.Dequeue());
        }
    }
}
