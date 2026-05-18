using AiAgentChallenge.Application.Tasks;

namespace AiAgentChallenge.Application.Abstractions;

public interface ITestCommandResolver
{
    TestCommandResolution Resolve(string repositoryPath, string testCommand);
}
