using System.Diagnostics;
using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Application.Tasks;
using AiAgentChallenge.Domain;
using AiAgentChallenge.Infrastructure.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.Infrastructure.Testing;

public sealed class ProcessBasedBuildRunner : IBuildRunner
{
    private readonly IProcessRunner _processRunner;
    private readonly ISecretRedactor _secretRedactor;
    private readonly BuildRunnerOptions _options;
    private readonly ILogger<ProcessBasedBuildRunner> _logger;

    public ProcessBasedBuildRunner(
        IProcessRunner processRunner,
        ISecretRedactor secretRedactor,
        IOptions<BuildRunnerOptions> options,
        ILogger<ProcessBasedBuildRunner> logger)
    {
        _processRunner = processRunner;
        _secretRedactor = secretRedactor;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<BuildResult> RunAsync(
        string repositoryPath,
        RepositoryAnalysis repositoryAnalysis,
        int attemptNumber,
        CancellationToken cancellationToken = default)
    {
        var command = ResolveCommand(repositoryPath, repositoryAnalysis);
        if (!command.IsSupported)
        {
            _logger.LogInformation(
                "Skipping build run attempt {AttemptNumber} for repository {RepositoryPath}: {Reason}",
                attemptNumber,
                repositoryPath,
                command.Reason);
            return new BuildResult
            {
                Command = command.DisplayCommand,
                Status = command.Status,
                ExitCode = -1,
                Duration = TimeSpan.Zero,
                Stdout = string.Empty,
                Stderr = command.Reason,
                AttemptNumber = attemptNumber
            };
        }

        _logger.LogInformation(
            "Starting build run attempt {AttemptNumber} with command {BuildCommand}",
            attemptNumber,
            command.DisplayCommand);
        var stopwatch = Stopwatch.StartNew();
        var result = await _processRunner.RunAsync(new ProcessExecutionRequest
        {
            FileName = command.Executable,
            Arguments = command.Arguments,
            WorkingDirectory = repositoryPath,
            Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds))
        }, cancellationToken);
        stopwatch.Stop();

        var stdout = Truncate(_secretRedactor.Redact(result.StandardOutput));
        var stderr = Truncate(_secretRedactor.Redact(result.StandardError));
        var buildResult = new BuildResult
        {
            Command = command.DisplayCommand,
            Status = result.ExitCode == 0 && !result.TimedOut
                ? BuildExecutionStatus.Passed
                : BuildExecutionStatus.Failed,
            ExitCode = result.ExitCode,
            Duration = stopwatch.Elapsed,
            Stdout = stdout,
            StdoutLines = ReportTextFormatter.ToLines(stdout),
            Stderr = stderr,
            AttemptNumber = attemptNumber
        };

        _logger.LogInformation(
            "Completed build run attempt {AttemptNumber} with status {BuildStatus} and exit code {ExitCode}",
            attemptNumber,
            buildResult.Status,
            buildResult.ExitCode);
        LogBuildResultSummary(buildResult);

        return buildResult;
    }

    private BuildCommandResolution ResolveCommand(string repositoryPath, RepositoryAnalysis repositoryAnalysis)
    {
        if (!string.Equals(repositoryAnalysis.BuildTool, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return BuildCommandResolution.Skipped("Build validation is only supported for .NET repositories in this iteration.");
        }

        var solutionFile = repositoryAnalysis.ProjectFiles
            .FirstOrDefault(path => path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                                    path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(solutionFile))
        {
            return BuildCommandResolution.Supported(
                $"dotnet build {solutionFile}",
                "dotnet",
                new[] { "build", solutionFile });
        }

        if (repositoryAnalysis.ProjectFiles.Any(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            return BuildCommandResolution.Supported(
                "dotnet build",
                "dotnet",
                new[] { "build" });
        }

        return BuildCommandResolution.Unsupported("dotnet build", "No solution or project file was available for build validation.");
    }

    private string Truncate(string value)
    {
        if (value.Length <= _options.MaxOutputChars)
        {
            return value;
        }

        return value[.._options.MaxOutputChars] + "... [truncated]";
    }

    private void LogBuildResultSummary(BuildResult buildResult)
    {
        var logLevel = buildResult.Status == BuildExecutionStatus.Failed ? LogLevel.Warning : LogLevel.Information;
        _logger.Log(
            logLevel,
            "Build result summary: command={BuildCommand}; status={BuildStatus}; exitCode={ExitCode}; durationMs={DurationMs}; attemptNumber={AttemptNumber}; stdoutFirstLines={StdoutFirstLines}; stderrFirstLines={StderrFirstLines}; stdoutLength={StdoutLength}; stderrLength={StderrLength}",
            buildResult.Command,
            buildResult.Status,
            buildResult.ExitCode,
            (long)buildResult.Duration.TotalMilliseconds,
            buildResult.AttemptNumber,
            ExecutionOutputLogFormatter.BuildFirstLinesSummary(buildResult.Stdout),
            ExecutionOutputLogFormatter.BuildFirstLinesSummary(buildResult.Stderr),
            buildResult.Stdout.Length,
            buildResult.Stderr.Length);
    }

    private sealed class BuildCommandResolution
    {
        private BuildCommandResolution(
            bool isSupported,
            string displayCommand,
            string executable,
            IReadOnlyList<string> arguments,
            string reason,
            BuildExecutionStatus status)
        {
            IsSupported = isSupported;
            DisplayCommand = displayCommand;
            Executable = executable;
            Arguments = arguments;
            Reason = reason;
            Status = status;
        }

        public bool IsSupported { get; }

        public string DisplayCommand { get; }

        public string Executable { get; }

        public IReadOnlyList<string> Arguments { get; }

        public string Reason { get; }

        public BuildExecutionStatus Status { get; }

        public static BuildCommandResolution Supported(string displayCommand, string executable, IReadOnlyList<string> arguments)
        {
            return new BuildCommandResolution(true, displayCommand, executable, arguments, string.Empty, BuildExecutionStatus.Passed);
        }

        public static BuildCommandResolution Skipped(string reason)
        {
            return new BuildCommandResolution(false, string.Empty, string.Empty, Array.Empty<string>(), reason, BuildExecutionStatus.Skipped);
        }

        public static BuildCommandResolution Unsupported(string displayCommand, string reason)
        {
            return new BuildCommandResolution(false, displayCommand, string.Empty, Array.Empty<string>(), reason, BuildExecutionStatus.Unsupported);
        }
    }
}
