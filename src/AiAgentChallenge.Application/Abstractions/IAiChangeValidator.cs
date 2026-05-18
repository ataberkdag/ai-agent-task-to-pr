using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Application.Abstractions;

public interface IAiChangeValidator
{
    AiChangeValidationResult Validate(
        string repositoryPath,
        RepositoryAnalysis repositoryAnalysis,
        AiCodeChangeResult aiCodeChangeResult);
}
