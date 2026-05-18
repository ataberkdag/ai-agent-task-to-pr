namespace AiAgentChallenge.Application.Tasks;

public sealed class GitCloneResult
{
    private GitCloneResult(
        bool isSuccess,
        GitCloneErrorCode errorCode,
        string message,
        int exitCode,
        string standardOutput,
        string standardError)
    {
        IsSuccess = isSuccess;
        ErrorCode = errorCode;
        Message = message;
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    public bool IsSuccess { get; }

    public GitCloneErrorCode ErrorCode { get; }

    public string Message { get; }

    public int ExitCode { get; }

    public string StandardOutput { get; }

    public string StandardError { get; }

    public static GitCloneResult Success(int exitCode, string standardOutput, string standardError)
    {
        return new GitCloneResult(true, GitCloneErrorCode.None, "Repository cloned successfully.", exitCode, standardOutput, standardError);
    }

    public static GitCloneResult Failure(
        GitCloneErrorCode errorCode,
        string message,
        int exitCode,
        string standardOutput,
        string standardError)
    {
        return new GitCloneResult(false, errorCode, message, exitCode, standardOutput, standardError);
    }
}
