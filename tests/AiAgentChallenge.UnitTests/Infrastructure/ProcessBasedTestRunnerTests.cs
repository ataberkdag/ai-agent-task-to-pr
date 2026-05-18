using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Application.Tasks;
using AiAgentChallenge.Domain;
using AiAgentChallenge.Infrastructure.Ai;
using AiAgentChallenge.Infrastructure.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class ProcessBasedTestRunnerTests
{
    [Fact]
    public async Task RunAsync_ExitCodeZero_ReturnsPassed()
    {
        var runner = CreateRunner(new ProcessExecutionResult
        {
            ExitCode = 0,
            StandardOutput = "ok",
            StandardError = string.Empty
        });

        var result = await runner.RunAsync(@"C:\repo", "dotnet test", 1);

        Assert.Equal(TestExecutionStatus.Passed, result.Status);
    }

    [Fact]
    public async Task RunAsync_NonZeroExitCode_ReturnsFailed()
    {
        var runner = CreateRunner(new ProcessExecutionResult
        {
            ExitCode = 1,
            StandardOutput = "fail",
            StandardError = "error"
        });

        var result = await runner.RunAsync(@"C:\repo", "dotnet test", 1);

        Assert.Equal(TestExecutionStatus.Failed, result.Status);
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

        var result = await runner.RunAsync(@"C:\repo", "dotnet test", 1);

        Assert.Contains("[truncated]", result.Stdout);
        Assert.Contains("[REDACTED]", result.Stderr);
    }

    [Fact]
    public async Task RunAsync_BuildsStdoutLines_WithNormalizedLineEndings()
    {
        var runner = CreateRunner(new ProcessExecutionResult
        {
            ExitCode = 0,
            StandardOutput = "line 1\r\nline 2\rline 3\n\nsummary\r\n",
            StandardError = string.Empty
        });

        var result = await runner.RunAsync(@"C:\repo", "dotnet test", 1);

        Assert.Equal(
            new[] { "line 1", "line 2", "line 3", string.Empty, "summary" },
            result.StdoutLines);
    }

    [Fact]
    public async Task RunAsync_LogsStructuredTestSummary()
    {
        var logger = new ListLogger<ProcessBasedTestRunner>();
        var runner = CreateRunner(new ProcessExecutionResult
        {
            ExitCode = 1,
            StandardOutput = "test line 1\ntest line 2\ntest line 3\ntest line 4",
            StandardError = "assert failed"
        }, logger: logger);

        await runner.RunAsync(@"C:\repo", "dotnet test", 1);

        Assert.Contains(logger.Entries, entry => entry.Message.Contains("Test result summary:", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("stdoutFirstLines=test line 1 | test line 2 | test line 3", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    private static ProcessBasedTestRunner CreateRunner(ProcessExecutionResult processExecutionResult, int maxOutputChars = 200, Microsoft.Extensions.Logging.ILogger<ProcessBasedTestRunner>? logger = null)
    {
        return new ProcessBasedTestRunner(
            new FakeTestCommandResolver(TestCommandResolution.Supported("dotnet test", "dotnet", new[] { "test" })),
            new FakeProcessRunner(processExecutionResult),
            new RegexBasedSecretRedactor(),
            Options.Create(new TestRunnerOptions
            {
                TimeoutSeconds = 30,
                MaxOutputChars = maxOutputChars
            }),
            logger ?? NullLogger<ProcessBasedTestRunner>.Instance);
    }

    private sealed class FakeTestCommandResolver : ITestCommandResolver
    {
        private readonly TestCommandResolution _resolution;

        public FakeTestCommandResolver(TestCommandResolution resolution)
        {
            _resolution = resolution;
        }

        public TestCommandResolution Resolve(string repositoryPath, string testCommand) => _resolution;
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly ProcessExecutionResult _result;

        public FakeProcessRunner(ProcessExecutionResult result)
        {
            _result = result;
        }

        public Task<ProcessExecutionResult> RunAsync(
            ProcessExecutionRequest request,
            CancellationToken cancellationToken = default)
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
