namespace AiAgentChallenge.Application.Tasks;

public sealed class DotNetSolutionSyncResult
{
    private DotNetSolutionSyncResult(
        bool isSuccess,
        string message,
        string solutionPath,
        IReadOnlyList<string> addedProjects,
        IReadOnlyList<string> removedProjects)
    {
        IsSuccess = isSuccess;
        Message = message;
        SolutionPath = solutionPath;
        AddedProjects = addedProjects;
        RemovedProjects = removedProjects;
    }

    public bool IsSuccess { get; }

    public string Message { get; }

    public string SolutionPath { get; }

    public IReadOnlyList<string> AddedProjects { get; }

    public IReadOnlyList<string> RemovedProjects { get; }

    public static DotNetSolutionSyncResult Success(
        string solutionPath,
        IReadOnlyList<string> addedProjects,
        IReadOnlyList<string> removedProjects,
        string? message = null)
    {
        return new DotNetSolutionSyncResult(
            true,
            message ?? "Solution sync completed successfully.",
            solutionPath,
            addedProjects,
            removedProjects);
    }

    public static DotNetSolutionSyncResult Failure(string solutionPath, string message)
    {
        return new DotNetSolutionSyncResult(
            false,
            message,
            solutionPath,
            Array.Empty<string>(),
            Array.Empty<string>());
    }
}
