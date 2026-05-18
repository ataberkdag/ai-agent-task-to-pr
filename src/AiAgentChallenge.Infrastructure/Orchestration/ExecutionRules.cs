using AiAgentChallenge.Domain;
using AiAgentChallenge.Infrastructure.Ai;

namespace AiAgentChallenge.Infrastructure.Orchestration;

internal interface INeedsFormattingRegenerationRule
{
    bool ShouldRegenerate(ExecutionContext context, AiChangeValidationResult validationResult);
}

internal interface INeedsAiFixRule
{
    bool ShouldAttemptFix(ExecutionContext context, TestResult firstTestResult);
}

internal interface ICanRunGitFinalizationRule
{
    bool CanRun(ExecutionContext context);
}

internal sealed class NeedsFormattingRegenerationRule : INeedsFormattingRegenerationRule
{
    public bool ShouldRegenerate(ExecutionContext context, AiChangeValidationResult validationResult)
    {
        return AiCodeFormattingHeuristics.ContainsCollapsedSourceError(validationResult.Errors);
    }
}

internal sealed class NeedsAiFixRule : INeedsAiFixRule
{
    private readonly AiOptions _aiOptions;

    public NeedsAiFixRule(Microsoft.Extensions.Options.IOptions<AiOptions> aiOptions)
    {
        _aiOptions = aiOptions.Value;
    }

    public bool ShouldAttemptFix(ExecutionContext context, TestResult firstTestResult)
    {
        return firstTestResult.Status == TestExecutionStatus.Failed &&
               _aiOptions.MaxTestFixAttempts > 0;
    }
}

internal sealed class CanRunGitFinalizationRule : ICanRunGitFinalizationRule
{
    public bool CanRun(ExecutionContext context)
    {
        return context.FinalTestStatus == TestExecutionStatus.Passed &&
               context.FinalBuildStatus != BuildExecutionStatus.Failed;
    }
}
