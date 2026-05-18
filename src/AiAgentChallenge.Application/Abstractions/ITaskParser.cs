using AiAgentChallenge.Application.Tasks;

namespace AiAgentChallenge.Application.Abstractions;

public interface ITaskParser
{
    Task<TaskParseResult> ParseAsync(string description, CancellationToken cancellationToken = default);
}
