namespace AiAgentChallenge.Application.Tasks;

public sealed class AsyncTaskSubmissionResult
{
    private AsyncTaskSubmissionResult(
        bool isSuccess,
        bool isQueueFull,
        AsyncTaskSubmissionAck? ack,
        IReadOnlyDictionary<string, string[]> errors)
    {
        IsSuccess = isSuccess;
        IsQueueFull = isQueueFull;
        Ack = ack;
        Errors = errors;
    }

    public bool IsSuccess { get; }

    public bool IsQueueFull { get; }

    public AsyncTaskSubmissionAck? Ack { get; }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public static AsyncTaskSubmissionResult Success(AsyncTaskSubmissionAck ack)
    {
        return new AsyncTaskSubmissionResult(true, false, ack, new Dictionary<string, string[]>());
    }

    public static AsyncTaskSubmissionResult ValidationFailure(IReadOnlyDictionary<string, string[]> errors)
    {
        return new AsyncTaskSubmissionResult(false, false, null, errors);
    }

    public static AsyncTaskSubmissionResult QueueFull(string message)
    {
        return new AsyncTaskSubmissionResult(
            false,
            true,
            null,
            new Dictionary<string, string[]>
            {
                ["Queue"] = new[] { message }
            });
    }
}
