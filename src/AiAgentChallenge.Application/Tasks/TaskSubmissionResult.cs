using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Application.Tasks;

public sealed class TaskSubmissionResult
{
    private TaskSubmissionResult(
        bool isSuccess,
        ExecutionReport? report,
        IReadOnlyDictionary<string, string[]> errors)
    {
        IsSuccess = isSuccess;
        Report = report;
        Errors = errors;
    }

    public bool IsSuccess { get; }

    public ExecutionReport? Report { get; }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public static TaskSubmissionResult Success(ExecutionReport report)
    {
        return new TaskSubmissionResult(true, report, new Dictionary<string, string[]>());
    }

    public static TaskSubmissionResult Failure(IReadOnlyDictionary<string, string[]> errors)
    {
        return new TaskSubmissionResult(false, null, errors);
    }
}
