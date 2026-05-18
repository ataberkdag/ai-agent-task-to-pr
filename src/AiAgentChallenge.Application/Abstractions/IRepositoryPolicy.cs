using AiAgentChallenge.Application.Tasks;

namespace AiAgentChallenge.Application.Abstractions;

public interface IRepositoryPolicy
{
    RepositoryPolicyResult Validate(string repositoryUrl);
}
