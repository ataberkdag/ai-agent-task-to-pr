using System.Diagnostics;
using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Application.Tasks;
using AiAgentChallenge.Domain;
using AiAgentChallenge.Infrastructure.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.Infrastructure.Testing;

public sealed class ProcessBasedTestRunner : ITestRunner
{
    private readonly ITestCommandResolver _testCommandResolver;
    private readonly IProcessRunner _processRunner;
    private readonly ISecretRedactor _secretRedactor;
    private readonly TestRunnerOptions _options;
    private readonly ILogger<ProcessBasedTestRunner> _logger;

    public ProcessBasedTestRunner(
        ITestCommandResolver testCommandResolver,
        IProcessRunner processRunner,
        ISecretRedactor secretRedactor,
        IOptions<TestRunnerOptions> options,
        ILogger<ProcessBasedTestRunner> logger)
    {
        _testCommandResolver = testCommandResolver;
        _processRunner = processRunner;
        _secretRedactor = secretRedactor;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TestResult> RunAsync(
        string repositoryPath,
        string testCommand,
        int attemptNumber,
        CancellationToken cancellationToken = default)
    {
        var resolution = _testCommandResolver.Resolve(repositoryPath, testCommand);
        if (!resolution.IsSupported)
        {
            _logger.LogError("Test command {Command} is unsupported for repository {RepositoryPath}", testCommand, repositoryPath);
            return new TestResult
            {
                Command = resolution.NormalizedCommand,
                Status = TestExecutionStatus.Unsupported,
                ExitCode = -1,
                Duration = TimeSpan.Zero,
                Stdout = string.Empty,
                Stderr = resolution.Reason,
                AttemptNumber = attemptNumber
            };
        }

        _logger.LogInformation("Starting test run attempt {AttemptNumber} with command {Command}", attemptNumber, resolution.NormalizedCommand);
        var stopwatch = Stopwatch.StartNew();
        var result = await _processRunner.RunAsync(new ProcessExecutionRequest
        {
            FileName = resolution.Executable,
            Arguments = resolution.Arguments,
            WorkingDirectory = repositoryPath,
            Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds))
        }, cancellationToken);
        stopwatch.Stop();

        var stdout = Truncate(_secretRedactor.Redact(result.StandardOutput));
        var stderr = Truncate(_secretRedactor.Redact(result.StandardError));

        var testResult = new TestResult
        {
            Command = resolution.NormalizedCommand,
            Status = result.ExitCode == 0 && !result.TimedOut
                ? TestExecutionStatus.Passed
                : TestExecutionStatus.Failed,
            ExitCode = result.ExitCode,
            Duration = stopwatch.Elapsed,
            Stdout = stdout,
            StdoutLines = ReportTextFormatter.ToLines(stdout),
            Stderr = stderr,
            AttemptNumber = attemptNumber
        };

        _logger.LogInformation(
            "Completed test run attempt {AttemptNumber} with status {TestStatus} and exit code {ExitCode} in {DurationMs}ms",
            attemptNumber,
            testResult.Status,
            testResult.ExitCode,
            (long)testResult.Duration.TotalMilliseconds);
        LogTestResultSummary(testResult);

        return testResult;
    }

    private string Truncate(string value)
    {
        if (value.Length <= _options.MaxOutputChars)
        {
            return value;
        }

        return value[.._options.MaxOutputChars] + "... [truncated]";
    }

    private void LogTestResultSummary(TestResult testResult)
    {
        var logLevel = testResult.Status == TestExecutionStatus.Failed ? LogLevel.Warning : LogLevel.Information;
        _logger.Log(
            logLevel,
            "Test result summary: command={TestCommand}; status={TestStatus}; exitCode={ExitCode}; durationMs={DurationMs}; attemptNumber={AttemptNumber}; stdoutFirstLines={StdoutFirstLines}; stderrFirstLines={StderrFirstLines}; stdoutLength={StdoutLength}; stderrLength={StderrLength}",
            testResult.Command,
            testResult.Status,
            testResult.ExitCode,
            (long)testResult.Duration.TotalMilliseconds,
            testResult.AttemptNumber,
            ExecutionOutputLogFormatter.BuildFirstLinesSummary(testResult.Stdout),
            ExecutionOutputLogFormatter.BuildFirstLinesSummary(testResult.Stderr),
            testResult.Stdout.Length,
            testResult.Stderr.Length);
    }
}
