using System.Diagnostics;
using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Application.Tasks;

namespace AiAgentChallenge.Infrastructure.Processes;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessExecutionResult> RunAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        using var process = new Process();

        process.StartInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory)
                ? Directory.GetCurrentDirectory()
                : request.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in request.Arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        foreach (var environmentVariable in request.EnvironmentVariables)
        {
            process.StartInfo.Environment[environmentVariable.Key] = environmentVariable.Value;
        }

        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        var waitForExitTask = process.WaitForExitAsync(cancellationToken);

        if (request.Timeout is { } timeout)
        {
            var completedTask = await Task.WhenAny(waitForExitTask, Task.Delay(timeout, cancellationToken));
            if (completedTask != waitForExitTask)
            {
                TryKill(process);

                return new ProcessExecutionResult
                {
                    ExitCode = -1,
                    StandardOutput = await standardOutputTask,
                    StandardError = await standardErrorTask,
                    TimedOut = true
                };
            }
        }
        else
        {
            await waitForExitTask;
        }

        await waitForExitTask;

        return new ProcessExecutionResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = await standardOutputTask,
            StandardError = await standardErrorTask,
            TimedOut = false
        };
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Ignore cleanup failures and return timeout result to caller.
        }
    }
}
