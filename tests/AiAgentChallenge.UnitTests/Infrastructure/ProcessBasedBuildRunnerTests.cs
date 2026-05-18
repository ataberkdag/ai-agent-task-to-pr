using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Application.Tasks;
using AiAgentChallenge.Domain;
using AiAgentChallenge.Infrastructure.Ai;
using AiAgentChallenge.Infrastructure.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class ProcessBasedBuildRunnerTests
{
    [Fact]
    public async Task RunAsync_DotNetSolution_ReturnsPassed()
    {
        var runner = CreateRunner(new ProcessExecutionResult
        {
            ExitCode = 0,
            StandardOutput = "build ok",
            StandardError = string.Empty
        });

        var result = await runner.RunAsync(
            @"C:\repo",
            new RepositoryAnalysis
            {
                BuildTool = "dotnet",
                ProjectFiles = new[] { "App.sln", "src/App/App.csproj" }
            },
            1);

        Assert.Equal(BuildExecutionStatus.Passed, result.Status);
        Assert.Equal("dotnet build App.sln", result.Command);
    }

    [Fact]
    public async Task RunAsync_NonDotNetRepository_ReturnsSkipped()
    {
        var runner = CreateRunner(new ProcessExecutionResult());

        var result = await runner.RunAsync(
            @"C:\repo",
            new RepositoryAnalysis
            {
                BuildTool = "npm"
            },
            1);

        Assert.Equal(BuildExecutionStatus.Skipped, result.Status);
    }

    [Fact]
    public async Task RunAsync_TruncatesAndRedactsOutput()
    {
        var longOutput = new string('a', 200);
        var runner = CreateRunner(new ProcessExecutionResult
        {
            ExitCode = 1,
            StandardOutput = longOutput,
            StandardError = "password=hunter2"
        }, maxOutputChars: 40);

        var result = await runner.RunAsync(
            @"C:\repo",
            new RepositoryAnalysis
            {
                BuildTool = "dotnet",
                ProjectFiles = new[] { "App.sln" }
            },
            1);

        Assert.Equal(BuildExecutionStatus.Failed, result.Status);
        Assert.Contains("[truncated]", result.Stdout);
        Assert.Contains("[REDACTED]", result.Stderr);
    }

    [Fact]
    public async Task RunAsync_LogsStructuredBuildSummary()
    {
        var logger = new ListLogger<ProcessBasedBuildRunner>();
        var runner = CreateRunner(new ProcessExecutionResult
        {
            ExitCode = 1,
            StandardOutput = "line 1\nline 2\nline 3\nline 4",
            StandardError = "compile failed"
        }, logger: logger);

        await runner.RunAsync(
            @"C:\repo",
            new RepositoryAnalysis
            {
                BuildTool = "dotnet",
                ProjectFiles = new[] { "App.sln" }
            },
            1);

        Assert.Contains(logger.Entries, entry => entry.Message.Contains("Build result summary:", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("stdoutFirstLines=line 1 | line 2 | line 3", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    private static ProcessBasedBuildRunner CreateRunner(ProcessExecutionResult processExecutionResult, int maxOutputChars = 200, Microsoft.Extensions.Logging.ILogger<ProcessBasedBuildRunner>? logger = null)
    {
        return new ProcessBasedBuildRunner(
            new FakeProcessRunner(processExecutionResult),
            new RegexBasedSecretRedactor(),
            Options.Create(new BuildRunnerOptions
            {
                TimeoutSeconds = 30,
                MaxOutputChars = maxOutputChars
            }),
            logger ?? NullLogger<ProcessBasedBuildRunner>.Instance);
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly ProcessExecutionResult _result;

        public FakeProcessRunner(ProcessExecutionResult result)
        {
            _result = result;
        }

        public Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class ListLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
