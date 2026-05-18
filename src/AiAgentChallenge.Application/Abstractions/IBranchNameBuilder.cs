namespace AiAgentChallenge.Application.Abstractions;

public interface IBranchNameBuilder
{
    string Build(string taskId, string title);
}
