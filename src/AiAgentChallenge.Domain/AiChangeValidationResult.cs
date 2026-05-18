namespace AiAgentChallenge.Domain;

public sealed class AiChangeValidationResult
{
    private AiChangeValidationResult(
        bool isSuccess,
        IReadOnlyList<AiChangedFile> validatedChanges,
        IReadOnlyList<string> errors,
        IReadOnlyList<AiChangeWarning> warnings)
    {
        IsSuccess = isSuccess;
        ValidatedChanges = validatedChanges;
        Errors = errors;
        Warnings = warnings;
    }

    public bool IsSuccess { get; }

    public IReadOnlyList<AiChangedFile> ValidatedChanges { get; }

    public IReadOnlyList<string> Errors { get; }

    public IReadOnlyList<AiChangeWarning> Warnings { get; }

    public static AiChangeValidationResult Success(
        IReadOnlyList<AiChangedFile> validatedChanges,
        IReadOnlyList<AiChangeWarning> warnings)
    {
        return new AiChangeValidationResult(true, validatedChanges, Array.Empty<string>(), warnings);
    }

    public static AiChangeValidationResult Failure(params string[] errors)
    {
        return new AiChangeValidationResult(false, Array.Empty<AiChangedFile>(), errors, Array.Empty<AiChangeWarning>());
    }
}
