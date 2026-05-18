using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Application.Abstractions;

public interface IFileChangeApplier
{
    Task<IReadOnlyList<string>> ApplyAsync(
        string repositoryPath,
        IReadOnlyList<AiChangedFile> changes,
        CancellationToken cancellationToken = default);
}
