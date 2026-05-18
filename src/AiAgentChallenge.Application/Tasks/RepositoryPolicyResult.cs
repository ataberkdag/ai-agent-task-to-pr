namespace AiAgentChallenge.Application.Tasks;

public sealed class RepositoryPolicyResult
{
    private RepositoryPolicyResult(
        bool isSuccess,
        RepositoryPolicyErrorCode errorCode,
        string message,
        string repositoryOwner)
    {
        IsSuccess = isSuccess;
        ErrorCode = errorCode;
        Message = message;
        RepositoryOwner = repositoryOwner;
    }

    public bool IsSuccess { get; }

    public RepositoryPolicyErrorCode ErrorCode { get; }

    public string Message { get; }

    public string RepositoryOwner { get; }

    public static RepositoryPolicyResult Success(string repositoryOwner)
    {
        return new RepositoryPolicyResult(true, RepositoryPolicyErrorCode.None, "Repository policy validation succeeded.", repositoryOwner);
    }

    public static RepositoryPolicyResult Failure(RepositoryPolicyErrorCode errorCode, string message)
    {
        return new RepositoryPolicyResult(false, errorCode, message, string.Empty);
    }
}
