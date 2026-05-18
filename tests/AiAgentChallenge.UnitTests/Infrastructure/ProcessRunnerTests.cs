using AiAgentChallenge.Application.Tasks;
using AiAgentChallenge.Infrastructure.Processes;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_ExecutesProcessAndCapturesOutput()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(new ProcessExecutionRequest
        {
            FileName = "dotnet",
            Arguments = new[] { "--version" },
            WorkingDirectory = AppContext.BaseDirectory,
            Timeout = TimeSpan.FromSeconds(30)
        });

        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardOutput));
    }
}
