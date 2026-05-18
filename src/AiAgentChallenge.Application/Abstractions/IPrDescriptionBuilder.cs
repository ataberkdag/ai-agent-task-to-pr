using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Application.Abstractions;

public interface IPrDescriptionBuilder
{
    string Build(ExecutionReport report);
}
