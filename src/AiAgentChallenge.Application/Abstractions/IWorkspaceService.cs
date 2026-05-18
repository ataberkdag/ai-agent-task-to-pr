using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Application.Abstractions;

public interface IWorkspaceService
{
    Task<WorkspaceInfo> CreateAsync(string taskId, CancellationToken cancellationToken = default);
}
