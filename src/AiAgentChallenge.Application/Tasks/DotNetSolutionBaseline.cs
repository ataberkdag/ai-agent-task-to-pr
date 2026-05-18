namespace AiAgentChallenge.Application.Tasks;

public sealed class DotNetSolutionBaseline
{
    private DotNetSolutionBaseline(
        bool isSupported,
        bool isSuccess,
        string message,
        string solutionPath,
        IReadOnlyList<string> existingProjectFiles,
        IReadOnlyList<string> baselineSolutionMembers)
    {
        IsSupported = isSupported;
        IsSuccess = isSuccess;
        Message = message;
        SolutionPath = solutionPath;
        ExistingProjectFiles = existingProjectFiles;
        BaselineSolutionMembers = baselineSolutionMembers;
    }

    public bool IsSupported { get; }

    public bool IsSuccess { get; }

    public string Message { get; }

    public string SolutionPath { get; }

    public IReadOnlyList<string> ExistingProjectFiles { get; }

    public IReadOnlyList<string> BaselineSolutionMembers { get; }

    public static DotNetSolutionBaseline Unsupported(string message)
    {
        return new DotNetSolutionBaseline(
            false,
            true,
            message,
            string.Empty,
            Array.Empty<string>(),
            Array.Empty<string>());
    }

    public static DotNetSolutionBaseline Failure(string message)
    {
        return new DotNetSolutionBaseline(
            true,
            false,
            message,
            string.Empty,
            Array.Empty<string>(),
            Array.Empty<string>());
    }

    public static DotNetSolutionBaseline Success(
        string solutionPath,
        IReadOnlyList<string> existingProjectFiles,
        IReadOnlyList<string> baselineSolutionMembers)
    {
        return new DotNetSolutionBaseline(
            true,
            true,
            "Solution baseline captured successfully.",
            solutionPath,
            existingProjectFiles,
            baselineSolutionMembers);
    }
}
