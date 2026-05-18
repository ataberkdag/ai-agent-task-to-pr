using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Application.Abstractions;

public interface ITestRunner
{
    Task<TestResult> RunAsync(
        string repositoryPath,
        string testCommand,
        int attemptNumber,
        CancellationToken cancellationToken = default);
}
