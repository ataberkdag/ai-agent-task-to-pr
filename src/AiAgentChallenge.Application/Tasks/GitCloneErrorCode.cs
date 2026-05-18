namespace AiAgentChallenge.Application.Tasks;

public enum GitCloneErrorCode
{
    None,
    BranchNotFound,
    PermissionDenied,
    CloneFailed,
    Timeout
}
