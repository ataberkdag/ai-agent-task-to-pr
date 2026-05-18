using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Application.Tasks;

public sealed class TaskParseResult
{
    private TaskParseResult(
        bool isSuccess,
        ParsedTask? parsedTask,
        IReadOnlyDictionary<string, string[]> errors)
    {
        IsSuccess = isSuccess;
        ParsedTask = parsedTask;
        Errors = errors;
    }

    public bool IsSuccess { get; }

    public ParsedTask? ParsedTask { get; }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public static TaskParseResult Success(ParsedTask parsedTask)
    {
        return new TaskParseResult(true, parsedTask, new Dictionary<string, string[]>());
    }

    public static TaskParseResult Failure(string key, string message)
    {
        return Failure(new Dictionary<string, string[]>
        {
            [key] = new[] { message }
        });
    }

    public static TaskParseResult Failure(IReadOnlyDictionary<string, string[]> errors)
    {
        return new TaskParseResult(false, null, errors);
    }
}
