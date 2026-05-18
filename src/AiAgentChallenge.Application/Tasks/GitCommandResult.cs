namespace AiAgentChallenge.Application.Tasks;

public sealed class GitCommandResult
{
    private GitCommandResult(
        bool isSuccess,
        string message,
        int exitCode,
        string standardOutput,
        string standardError)
    {
        IsSuccess = isSuccess;
        Message = message;
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    public bool IsSuccess { get; }

    public string Message { get; }

    public int ExitCode { get; }

    public string StandardOutput { get; }

    public string StandardError { get; }

    public static GitCommandResult Success(string message, int exitCode, string standardOutput, string standardError)
    {
        return new GitCommandResult(true, message, exitCode, standardOutput, standardError);
    }

    public static GitCommandResult Failure(string message, int exitCode, string standardOutput, string standardError)
    {
        return new GitCommandResult(false, message, exitCode, standardOutput, standardError);
    }
}
