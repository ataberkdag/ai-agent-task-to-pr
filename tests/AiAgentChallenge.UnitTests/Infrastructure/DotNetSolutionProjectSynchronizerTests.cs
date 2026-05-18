using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Application.Tasks;
using AiAgentChallenge.Domain;
using AiAgentChallenge.Infrastructure.Git;
using AiAgentChallenge.Infrastructure.Projects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class DotNetSolutionProjectSynchronizerTests
{
    [Fact]
    public async Task CaptureBaselineAsync_WhenNoSolutionFileExists_ReturnsUnsupported()
    {
        using var tempDirectory = new TemporaryDirectory();
        var synchronizer = CreateSynchronizer(new FakeProcessRunner());

        var result = await synchronizer.CaptureBaselineAsync(
            tempDirectory.Path,
            new RepositoryAnalysis
            {
                BuildTool = "dotnet",
                ProjectFiles = Array.Empty<string>()
            });

        Assert.False(result.IsSupported);
        Assert.True(result.IsSuccess);
        Assert.Contains("no solution file", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CaptureBaselineAsync_ParsesSolutionMembersAndSnapshotsExistingProjects()
    {
        using var tempDirectory = new TemporaryDirectory();
        WriteFile(tempDirectory.Path, "App.sln", string.Empty);
        WriteFile(tempDirectory.Path, Path.Combine("src", "App", "App.csproj"), "<Project />");
        WriteFile(tempDirectory.Path, Path.Combine("tests", "App.Tests", "App.Tests.csproj"), "<Project />");
        WriteFile(tempDirectory.Path, Path.Combine("sandbox", "Detached", "Detached.csproj"), "<Project />");

        var runner = new FakeProcessRunner(new ProcessExecutionResult
        {
            ExitCode = 0,
            StandardOutput = """
                Project(s)
                ----------
                src/App/App.csproj
                tests/App.Tests/App.Tests.csproj
                """
        });

        var synchronizer = CreateSynchronizer(runner);

        var result = await synchronizer.CaptureBaselineAsync(
            tempDirectory.Path,
            new RepositoryAnalysis
            {
                BuildTool = "dotnet",
                ProjectFiles = new[] { "App.sln", "src/App/App.csproj" }
            });

        Assert.True(result.IsSupported);
        Assert.True(result.IsSuccess);
        Assert.Equal(Path.Combine(tempDirectory.Path, "App.sln"), result.SolutionPath);
        Assert.Equal(
            new[]
            {
                "src/App/App.csproj",
                "tests/App.Tests/App.Tests.csproj"
            },
            result.BaselineSolutionMembers.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(
            new[]
            {
                "sandbox/Detached/Detached.csproj",
                "src/App/App.csproj",
                "tests/App.Tests/App.Tests.csproj"
            },
            result.ExistingProjectFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
        Assert.Collection(
            runner.Requests,
            request =>
            {
                Assert.Equal("dotnet", request.FileName);
                Assert.Equal(
                    new[]
                    {
                        "sln",
                        Path.Combine(tempDirectory.Path, "App.sln"),
                        "list"
                    },
                    request.Arguments);
            });
    }

    [Fact]
    public async Task SyncAsync_AddsOnlyNewProjects()
    {
        using var tempDirectory = new TemporaryDirectory();
        var solutionPath = Path.Combine(tempDirectory.Path, "App.sln");
        WriteFile(tempDirectory.Path, "App.sln", string.Empty);
        WriteFile(tempDirectory.Path, Path.Combine("src", "App", "App.csproj"), "<Project />");
        WriteFile(tempDirectory.Path, Path.Combine("legacy", "Detached", "Detached.csproj"), "<Project />");
        WriteFile(tempDirectory.Path, Path.Combine("tests", "App.Tests", "App.Tests.csproj"), "<Project />");

        var runner = new FakeProcessRunner(new ProcessExecutionResult
        {
            ExitCode = 0,
            StandardOutput = "added"
        });

        var synchronizer = CreateSynchronizer(runner);
        var baseline = DotNetSolutionBaseline.Success(
            solutionPath,
            new[]
            {
                "legacy/Detached/Detached.csproj",
                "src/App/App.csproj"
            },
            new[]
            {
                "src/App/App.csproj"
            });

        var result = await synchronizer.SyncAsync(tempDirectory.Path, baseline);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "tests/App.Tests/App.Tests.csproj" }, result.AddedProjects);
        Assert.Empty(result.RemovedProjects);
        Assert.Single(runner.Requests);
        Assert.Equal(
            new[]
            {
                "sln",
                solutionPath,
                "add",
                Path.Combine(tempDirectory.Path, "tests", "App.Tests", "App.Tests.csproj")
            },
            runner.Requests[0].Arguments);
    }

    [Fact]
    public async Task SyncAsync_RemovesDeletedSolutionMembers()
    {
        using var tempDirectory = new TemporaryDirectory();
        var solutionPath = Path.Combine(tempDirectory.Path, "App.sln");
        WriteFile(tempDirectory.Path, "App.sln", string.Empty);
        WriteFile(tempDirectory.Path, Path.Combine("src", "App", "App.csproj"), "<Project />");

        var runner = new FakeProcessRunner(new ProcessExecutionResult
        {
            ExitCode = 0,
            StandardOutput = "removed"
        });

        var synchronizer = CreateSynchronizer(runner);
        var baseline = DotNetSolutionBaseline.Success(
            solutionPath,
            new[]
            {
                "src/App/App.csproj",
                "tests/App.Tests/App.Tests.csproj"
            },
            new[]
            {
                "src/App/App.csproj",
                "tests/App.Tests/App.Tests.csproj"
            });

        var result = await synchronizer.SyncAsync(tempDirectory.Path, baseline);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.AddedProjects);
        Assert.Equal(new[] { "tests/App.Tests/App.Tests.csproj" }, result.RemovedProjects);
        Assert.Single(runner.Requests);
        Assert.Equal(
            new[]
            {
                "sln",
                solutionPath,
                "remove",
                "tests/App.Tests/App.Tests.csproj"
            },
            runner.Requests[0].Arguments);
    }

    [Fact]
    public async Task CaptureBaselineAsync_WhenOnlySlnxExists_UsesSlnxAsPrimarySolution()
    {
        using var tempDirectory = new TemporaryDirectory();
        WriteFile(tempDirectory.Path, "App.slnx", "<Solution />");
        WriteFile(tempDirectory.Path, Path.Combine("src", "App", "App.csproj"), "<Project />");

        var runner = new FakeProcessRunner(new ProcessExecutionResult
        {
            ExitCode = 0,
            StandardOutput = """
                Project(s)
                ----------
                src/App/App.csproj
                """
        });

        var synchronizer = CreateSynchronizer(runner);

        var result = await synchronizer.CaptureBaselineAsync(
            tempDirectory.Path,
            new RepositoryAnalysis
            {
                BuildTool = "dotnet",
                ProjectFiles = new[] { "App.slnx", "src/App/App.csproj" }
            });

        Assert.True(result.IsSupported);
        Assert.True(result.IsSuccess);
        Assert.Equal(Path.Combine(tempDirectory.Path, "App.slnx"), result.SolutionPath);
        Assert.Collection(
            runner.Requests,
            request =>
            {
                Assert.Equal(
                    new[]
                    {
                        "sln",
                        Path.Combine(tempDirectory.Path, "App.slnx"),
                        "list"
                    },
                    request.Arguments);
            });
    }

    private static DotNetSolutionProjectSynchronizer CreateSynchronizer(FakeProcessRunner runner)
    {
        return new DotNetSolutionProjectSynchronizer(
            runner,
            Options.Create(new GitOptions { PushTimeoutSeconds = 120 }),
            NullLogger<DotNetSolutionProjectSynchronizer>.Instance);
    }

    private static void WriteFile(string rootPath, string relativePath, string content)
    {
        var fullPath = Path.Combine(rootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly Queue<ProcessExecutionResult> _results;

        public FakeProcessRunner(params ProcessExecutionResult[] results)
        {
            _results = new Queue<ProcessExecutionResult>(results);
        }

        public List<ProcessExecutionRequest> Requests { get; } = new();

        public Task<ProcessExecutionResult> RunAsync(
            ProcessExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "AiAgentChallengeTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
