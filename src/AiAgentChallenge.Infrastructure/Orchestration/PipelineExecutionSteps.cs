using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Application.Tasks;
using AiAgentChallenge.Domain;
using AiAgentChallenge.Infrastructure.Text;
using Microsoft.Extensions.Logging;

namespace AiAgentChallenge.Infrastructure.Orchestration;

internal sealed class RepositoryPolicyValidationStep : IExecutionStep
{
    private readonly IRepositoryPolicy _repositoryPolicy;

    public RepositoryPolicyValidationStep(IRepositoryPolicy repositoryPolicy)
    {
        _repositoryPolicy = repositoryPolicy;
    }

    public int Order => 10;

    public Task<ExecutionStepResult> ExecuteAsync(ExecutionContext context, CancellationToken cancellationToken = default)
    {
        var step = context.Report.StartStep("RepositoryPolicyValidation");
        var policyResult = _repositoryPolicy.Validate(context.Request.ParsedTask.RepositoryUrl);
        if (!policyResult.IsSuccess)
        {
            context.CloneStatus = CloneStatus.Failed;
            context.Report.CompleteStep(step, ExecutionTimelineStatus.Failed, policyResult.Message);
            context.MarkFailure(policyResult.Message);
            return Task.FromResult(ExecutionStepResult.Stop());
        }

        context.Report.CompleteStep(step, ExecutionTimelineStatus.Succeeded, "Repository policy validation succeeded.");
        return Task.FromResult(ExecutionStepResult.Continue());
    }
}

internal sealed class WorkspaceCreationStep : IExecutionStep
{
    private readonly IWorkspaceService _workspaceService;

    public WorkspaceCreationStep(IWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService;
    }

    public int Order => 20;

    public async Task<ExecutionStepResult> ExecuteAsync(ExecutionContext context, CancellationToken cancellationToken = default)
    {
        var step = context.Report.StartStep("WorkspaceCreation");

        try
        {
            context.Workspace = await _workspaceService.CreateAsync(context.Request.TaskId, cancellationToken);
            context.Report.CompleteStep(step, ExecutionTimelineStatus.Succeeded, $"Workspace created at {context.Workspace.WorkspacePath}.");
            return ExecutionStepResult.Continue();
        }
        catch (Exception exception)
        {
            var message = $"Workspace creation failed: {exception.Message}";
            context.Report.CompleteStep(step, ExecutionTimelineStatus.Failed, message);
            context.MarkFailure(message);
            return ExecutionStepResult.Stop();
        }
    }
}

internal sealed class RepositoryCloneStep : IExecutionStep
{
    private readonly IGitClient _gitClient;

    public RepositoryCloneStep(IGitClient gitClient)
    {
        _gitClient = gitClient;
    }

    public int Order => 30;

    public async Task<ExecutionStepResult> ExecuteAsync(ExecutionContext context, CancellationToken cancellationToken = default)
    {
        var step = context.Report.StartStep("RepositoryClone");
        var workspace = context.Workspace!;
        var cloneResult = await _gitClient.CloneAsync(
            context.Request.ParsedTask.RepositoryUrl,
            context.Request.ParsedTask.BaseBranch,
            workspace.RepositoryPath,
            cancellationToken);

        if (!cloneResult.IsSuccess)
        {
            context.CloneStatus = CloneStatus.Failed;
            context.Report.CompleteStep(step, ExecutionTimelineStatus.Failed, cloneResult.Message);
            context.MarkFailure(cloneResult.Message);
            return ExecutionStepResult.Stop();
        }

        context.CloneStatus = CloneStatus.Succeeded;
        context.Report.CompleteStep(step, ExecutionTimelineStatus.Succeeded, "Repository clone succeeded.");
        return ExecutionStepResult.Continue();
    }
}

internal sealed class RepositoryAnalysisStep : IExecutionStep
{
    private readonly IRepositoryAnalyzer _repositoryAnalyzer;

    public RepositoryAnalysisStep(IRepositoryAnalyzer repositoryAnalyzer)
    {
        _repositoryAnalyzer = repositoryAnalyzer;
    }

    public int Order => 40;

    public async Task<ExecutionStepResult> ExecuteAsync(ExecutionContext context, CancellationToken cancellationToken = default)
    {
        var step = context.Report.StartStep("RepositoryAnalysis");

        try
        {
            context.RepositoryAnalysis = await _repositoryAnalyzer.AnalyzeAsync(
                context.Workspace!.RepositoryPath,
                context.Request.ParsedTask,
                cancellationToken);
            context.Report.CompleteStep(step, ExecutionTimelineStatus.Succeeded, "Repository analysis succeeded.");
            return ExecutionStepResult.Continue();
        }
        catch (Exception exception)
        {
            var message = $"Repository analysis failed: {exception.Message}";
            context.Report.CompleteStep(step, ExecutionTimelineStatus.Failed, message);
            context.MarkFailure(message);
            return ExecutionStepResult.Stop();
        }
    }
}

internal sealed class SolutionBaselineCaptureStep : IExecutionStep
{
    private readonly ISolutionProjectSynchronizer _solutionProjectSynchronizer;

    public SolutionBaselineCaptureStep(ISolutionProjectSynchronizer solutionProjectSynchronizer)
    {
        _solutionProjectSynchronizer = solutionProjectSynchronizer;
    }

    public int Order => 50;

    public async Task<ExecutionStepResult> ExecuteAsync(ExecutionContext context, CancellationToken cancellationToken = default)
    {
        var step = context.Report.StartStep("DotNetSolutionBaselineCapture");
        var baseline = await _solutionProjectSynchronizer.CaptureBaselineAsync(
            context.Workspace!.RepositoryPath,
            context.RepositoryAnalysis!,
            cancellationToken);

        context.SolutionBaseline = baseline;

        if (!baseline.IsSupported)
        {
            context.Report.CompleteStep(step, ExecutionTimelineStatus.Skipped, baseline.Message);
            return ExecutionStepResult.Continue();
        }

        if (!baseline.IsSuccess)
        {
            context.Report.CompleteStep(step, ExecutionTimelineStatus.Failed, baseline.Message);
            context.MarkFailure(baseline.Message);
            return ExecutionStepResult.Stop();
        }

        context.Report.CompleteStep(step, ExecutionTimelineStatus.Succeeded, $"Solution baseline captured for {baseline.SolutionPath}.");
        return ExecutionStepResult.Continue();
    }
}

internal sealed class AgentContextBuildStep : IExecutionStep
{
    private readonly IAgentContextBuilder _agentContextBuilder;

    public AgentContextBuildStep(IAgentContextBuilder agentContextBuilder)
    {
        _agentContextBuilder = agentContextBuilder;
    }

    public int Order => 60;

    public async Task<ExecutionStepResult> ExecuteAsync(ExecutionContext context, CancellationToken cancellationToken = default)
    {
        var step = context.Report.StartStep("AgentContextBuild");

        try
        {
            context.AgentContext = await _agentContextBuilder.BuildAsync(
                context.Workspace!.RepositoryPath,
                context.Request.ParsedTask,
                context.RepositoryAnalysis!,
                cancellationToken);
            context.Report.CompleteStep(step, ExecutionTimelineStatus.Succeeded, "Agent context build succeeded.");
            return ExecutionStepResult.Continue();
        }
        catch (Exception exception)
        {
            var message = $"Agent context build failed: {exception.Message}";
            context.Report.CompleteStep(step, ExecutionTimelineStatus.Failed, message);
            context.MarkFailure(message);
            return ExecutionStepResult.Stop();
        }
    }
}

internal sealed class AiChangePreparationStep : IExecutionStep
{
    private readonly IAiCodeAgent _aiCodeAgent;
    private readonly IAiChangeValidator _aiChangeValidator;
    private readonly INeedsFormattingRegenerationRule _formattingRule;

    public AiChangePreparationStep(
        IAiCodeAgent aiCodeAgent,
        IAiChangeValidator aiChangeValidator,
        INeedsFormattingRegenerationRule formattingRule)
    {
        _aiCodeAgent = aiCodeAgent;
        _aiChangeValidator = aiChangeValidator;
        _formattingRule = formattingRule;
    }

    public int Order => 70;

    public async Task<ExecutionStepResult> ExecuteAsync(ExecutionContext context, CancellationToken cancellationToken = default)
    {
        var generateStep = context.Report.StartStep("AiGenerateChanges");

        try
        {
            context.AiCodeChangeResult = await _aiCodeAgent.GenerateChangesAsync(context.AgentContext!, cancellationToken);
            context.AddWarnings(context.AiCodeChangeResult.Warnings);
            context.Report.CompleteStep(generateStep, ExecutionTimelineStatus.Succeeded, "AI code generation succeeded.");
        }
        catch (Exception exception)
        {
            var message = $"AI code generation failed: {exception.Message}";
            context.Report.CompleteStep(generateStep, ExecutionTimelineStatus.Failed, message);
            context.MarkFailure(message);
            return ExecutionStepResult.Stop();
        }

        var validateStep = context.Report.StartStep("AiValidateChanges");
        var validationResult = _aiChangeValidator.Validate(
            context.Workspace!.RepositoryPath,
            context.RepositoryAnalysis!,
            context.AiCodeChangeResult!);

        if (!validationResult.IsSuccess)
        {
            if (_formattingRule.ShouldRegenerate(context, validationResult))
            {
                var regenerateStep = context.Report.StartStep("AiRegenerateFormatting");

                try
                {
                    context.AiCodeChangeResult = await _aiCodeAgent.RegenerateFormattedChangesAsync(
                        context.AgentContext!,
                        context.AiCodeChangeResult!,
                        cancellationToken);
                    context.AddWarnings(context.AiCodeChangeResult.Warnings);

                    validationResult = _aiChangeValidator.Validate(
                        context.Workspace.RepositoryPath,
                        context.RepositoryAnalysis,
                        context.AiCodeChangeResult);

                    if (!validationResult.IsSuccess)
                    {
                        var message = $"AI formatting regeneration validation failed: {string.Join("; ", validationResult.Errors)}";
                        context.Report.CompleteStep(regenerateStep, ExecutionTimelineStatus.Failed, message);
                        context.Report.CompleteStep(validateStep, ExecutionTimelineStatus.Failed, message);
                        context.MarkFailure(message);
                        return ExecutionStepResult.Stop();
                    }

                    context.FormattingRegenerated = true;
                    context.Report.CompleteStep(regenerateStep, ExecutionTimelineStatus.Succeeded, "AI formatting regeneration succeeded.");
                }
                catch (Exception exception)
                {
                    var message = $"AI formatting regeneration failed: {exception.Message}";
                    context.Report.CompleteStep(regenerateStep, ExecutionTimelineStatus.Failed, message);
                    context.Report.CompleteStep(validateStep, ExecutionTimelineStatus.Failed, message);
                    context.MarkFailure(message);
                    return ExecutionStepResult.Stop();
                }
            }
            else
            {
                var message = $"AI change validation failed: {string.Join("; ", validationResult.Errors)}";
                context.Report.CompleteStep(validateStep, ExecutionTimelineStatus.Failed, message);
                context.MarkFailure(message);
                return ExecutionStepResult.Stop();
            }
        }
        else
        {
            context.Report.SkipStep("AiRegenerateFormatting", "Formatting regeneration was not needed because the AI output was already well-formed.");
        }

        context.ValidationResult = validationResult;
        context.AddWarnings(validationResult.Warnings);

        if (context.FormattingRegenerated)
        {
            context.AddWarningMessages(new[] { "AI output was reformatted via regeneration step" });
        }

        context.Report.CompleteStep(validateStep, ExecutionTimelineStatus.Succeeded, "AI changes validated successfully.");
        return ExecutionStepResult.Continue();
    }
}

internal sealed class ApplyChangesAndSyncStep : IExecutionStep
{
    private readonly IFileChangeApplier _fileChangeApplier;
    private readonly ISolutionProjectSynchronizer _solutionProjectSynchronizer;

    public ApplyChangesAndSyncStep(
        IFileChangeApplier fileChangeApplier,
        ISolutionProjectSynchronizer solutionProjectSynchronizer)
    {
        _fileChangeApplier = fileChangeApplier;
        _solutionProjectSynchronizer = solutionProjectSynchronizer;
    }

    public int Order => 80;

    public async Task<ExecutionStepResult> ExecuteAsync(ExecutionContext context, CancellationToken cancellationToken = default)
    {
        var applyStep = context.Report.StartStep("ApplyFileChanges");

        try
        {
            context.ChangedFiles = await _fileChangeApplier.ApplyAsync(
                context.Workspace!.RepositoryPath,
                context.ValidationResult!.ValidatedChanges,
                cancellationToken);
            context.Report.CompleteStep(applyStep, ExecutionTimelineStatus.Succeeded, $"Applied {context.ChangedFiles.Count} file change(s).");
        }
        catch (Exception exception)
        {
            var message = $"Applying AI file changes failed: {exception.Message}";
            context.Report.CompleteStep(applyStep, ExecutionTimelineStatus.Failed, message);
            context.MarkFailure(message);
            return ExecutionStepResult.Stop();
        }

        if (!context.SolutionBaseline.IsSupported)
        {
            context.Report.SkipStep(
                "DotNetSolutionSync",
                DotNetSolutionSyncDiagnostics.BuildSkippedMessage(context.SolutionBaseline.Message, context.ChangedFiles));
            return ExecutionStepResult.Continue();
        }

        var solutionSyncStep = context.Report.StartStep("DotNetSolutionSync");
        var syncResult = await _solutionProjectSynchronizer.SyncAsync(
            context.Workspace!.RepositoryPath,
            context.SolutionBaseline,
            cancellationToken);

        if (!syncResult.IsSuccess)
        {
            context.Report.CompleteStep(solutionSyncStep, ExecutionTimelineStatus.Failed, syncResult.Message);
            context.MarkFailure(syncResult.Message);
            return ExecutionStepResult.Stop();
        }

        foreach (var project in syncResult.AddedProjects)
        {
            context.AddedProjectsToSolution.Add(project);
        }

        foreach (var project in syncResult.RemovedProjects)
        {
            context.RemovedProjectsFromSolution.Add(project);
        }

        context.Report.CompleteStep(
            solutionSyncStep,
            ExecutionTimelineStatus.Succeeded,
            DotNetSolutionSyncDiagnostics.BuildCompletionMessage(syncResult.Message, context.ChangedFiles, syncResult));
        return ExecutionStepResult.Continue();
    }
}

internal sealed class TestAndFixWorkflowStep : IExecutionStep
{
    private readonly IBuildRunner _buildRunner;
    private readonly ITestRunner _testRunner;
    private readonly IAiCodeAgent _aiCodeAgent;
    private readonly IAiChangeValidator _aiChangeValidator;
    private readonly IFileChangeApplier _fileChangeApplier;
    private readonly ISolutionProjectSynchronizer _solutionProjectSynchronizer;
    private readonly INeedsAiFixRule _needsAiFixRule;
    private readonly ILogger<TestAndFixWorkflowStep> _logger;

    public TestAndFixWorkflowStep(
        IBuildRunner buildRunner,
        ITestRunner testRunner,
        IAiCodeAgent aiCodeAgent,
        IAiChangeValidator aiChangeValidator,
        IFileChangeApplier fileChangeApplier,
        ISolutionProjectSynchronizer solutionProjectSynchronizer,
        INeedsAiFixRule needsAiFixRule,
        ILogger<TestAndFixWorkflowStep> logger)
    {
        _buildRunner = buildRunner;
        _testRunner = testRunner;
        _aiCodeAgent = aiCodeAgent;
        _aiChangeValidator = aiChangeValidator;
        _fileChangeApplier = fileChangeApplier;
        _solutionProjectSynchronizer = solutionProjectSynchronizer;
        _needsAiFixRule = needsAiFixRule;
        _logger = logger;
    }

    public int Order => 90;

    public async Task<ExecutionStepResult> ExecuteAsync(ExecutionContext context, CancellationToken cancellationToken = default)
    {
        var buildAttempt1Step = context.Report.StartStep("BuildValidationAttempt1");
        var firstBuildResult = await _buildRunner.RunAsync(
            context.Workspace!.RepositoryPath,
            context.RepositoryAnalysis!,
            1,
            cancellationToken);
        context.BuildResults.Add(firstBuildResult);
        context.FinalBuildStatus = firstBuildResult.Status;
        context.Report.CompleteStep(
            buildAttempt1Step,
            firstBuildResult.Status is BuildExecutionStatus.Passed or BuildExecutionStatus.Skipped or BuildExecutionStatus.Unsupported
                ? ExecutionTimelineStatus.Succeeded
                : ExecutionTimelineStatus.Failed,
            $"Build attempt 1 finished with status {firstBuildResult.Status}.");
        LogBuildAttempt("BuildValidationAttempt1", firstBuildResult);

        if (firstBuildResult.Status == BuildExecutionStatus.Failed)
        {
            context.FailureReason = $"Build failed on attempt {firstBuildResult.AttemptNumber}.";
        }

        if (firstBuildResult.Status is BuildExecutionStatus.Passed or BuildExecutionStatus.Skipped)
        {
            context.FinalBuildStatus = firstBuildResult.Status;
        }

        var testAttempt1Step = context.Report.StartStep("TestRunAttempt1");
        TestResult firstTestResult;
        if (firstBuildResult.Status == BuildExecutionStatus.Failed)
        {
            firstTestResult = new TestResult
            {
                Command = context.RepositoryAnalysis!.TestCommand,
                Status = TestExecutionStatus.Failed,
                ExitCode = -1,
                Duration = TimeSpan.Zero,
                Stdout = "Tests not executed because build failed.",
                StdoutLines = new[] { "Tests not executed because build failed." },
                Stderr = firstBuildResult.Stderr,
                AttemptNumber = 1
            };
        }
        else
        {
            firstTestResult = await _testRunner.RunAsync(
                context.Workspace!.RepositoryPath,
                context.RepositoryAnalysis!.TestCommand,
                1,
                cancellationToken);
        }

        context.TestResults.Add(firstTestResult);
        context.FinalTestStatus = firstTestResult.Status;
        context.Report.CompleteStep(
            testAttempt1Step,
            firstTestResult.Status == TestExecutionStatus.Passed ? ExecutionTimelineStatus.Succeeded : ExecutionTimelineStatus.Failed,
            $"Test attempt 1 finished with status {firstTestResult.Status}.");
        LogTestAttempt("TestRunAttempt1", firstTestResult, firstBuildResult.Status == BuildExecutionStatus.Failed ? "BuildFailed" : string.Empty);

        if (firstBuildResult.Status != BuildExecutionStatus.Failed && firstTestResult.Status == TestExecutionStatus.Passed)
        {
            context.Report.SkipStep("AiFixAttempt", "AI fix attempt was not needed because the first test run passed.");
            context.Report.SkipStep("ApplyFixChanges", "AI fix changes were not needed because the first test run passed.");
            context.Report.SkipStep("DotNetSolutionSyncAfterFix", "Solution sync after fix was not needed because the first test run passed.");
            context.Report.SkipStep("BuildValidationAttempt2", "Second build attempt was not needed because the first test run passed.");
            context.Report.SkipStep("TestRunAttempt2", "Second test attempt was not needed because the first test run passed.");
            return ExecutionStepResult.Continue();
        }

        if (firstBuildResult.Status != BuildExecutionStatus.Failed && firstTestResult.Status == TestExecutionStatus.Unsupported)
        {
            context.FailureReason = "Repository test command is unsupported.";
            context.Report.SkipStep("AiFixAttempt", "AI fix attempt skipped because the test command is unsupported.");
            context.Report.SkipStep("ApplyFixChanges", "AI fix changes were skipped because the test command is unsupported.");
            context.Report.SkipStep("DotNetSolutionSyncAfterFix", "Solution sync after fix was skipped because the test command is unsupported.");
            context.Report.SkipStep("BuildValidationAttempt2", "Second build attempt was skipped because the test command is unsupported.");
            context.Report.SkipStep("TestRunAttempt2", "Second test attempt was skipped because the test command is unsupported.");
            return ExecutionStepResult.Continue();
        }

        if (!_needsAiFixRule.ShouldAttemptFix(context, firstTestResult))
        {
            context.FailureReason = firstBuildResult.Status == BuildExecutionStatus.Failed
                ? "Build failed and AI fix attempts are disabled."
                : "Tests failed and AI fix attempts are disabled.";
            context.Report.SkipStep("AiFixAttempt", "AI fix attempt skipped because fix attempts are disabled.");
            context.Report.SkipStep("ApplyFixChanges", "AI fix changes were skipped because fix attempts are disabled.");
            context.Report.SkipStep("DotNetSolutionSyncAfterFix", "Solution sync after fix was skipped because fix attempts are disabled.");
            context.Report.SkipStep("BuildValidationAttempt2", "Second build attempt was skipped because fix attempts are disabled.");
            context.Report.SkipStep("TestRunAttempt2", "Second test attempt was skipped because fix attempts are disabled.");
            return ExecutionStepResult.Continue();
        }

        context.AiFixAttempted = true;
        var aiFixStep = context.Report.StartStep("AiFixAttempt");

        try
        {
            context.AiFixResult = await _aiCodeAgent.GenerateFixForTestFailureAsync(
                context.AgentContext!,
                context.AiCodeChangeResult!,
                firstBuildResult,
                firstTestResult,
                cancellationToken);
            context.Report.CompleteStep(aiFixStep, ExecutionTimelineStatus.Succeeded, "AI fix attempt generated changes.");
        }
        catch (Exception exception)
        {
            context.Report.CompleteStep(aiFixStep, ExecutionTimelineStatus.Failed, $"AI fix attempt failed: {exception.Message}");
            context.Report.SkipStep("ApplyFixChanges", "AI fix changes were not applied because the AI fix attempt failed.");
            context.Report.SkipStep("DotNetSolutionSyncAfterFix", "Solution sync after fix was skipped because the AI fix attempt failed.");
            context.Report.SkipStep("BuildValidationAttempt2", "Second build attempt was skipped because the AI fix attempt failed.");
            context.Report.SkipStep("TestRunAttempt2", "Second test attempt was skipped because the AI fix attempt failed.");
            context.FailureReason = $"AI fix attempt failed: {exception.Message}";
            return ExecutionStepResult.Continue();
        }

        context.AiFixValidationResult = _aiChangeValidator.Validate(
            context.Workspace.RepositoryPath,
            context.RepositoryAnalysis,
            context.AiFixResult!);

        if (!context.AiFixValidationResult.IsSuccess)
        {
            var message = $"AI fix validation failed: {string.Join("; ", context.AiFixValidationResult.Errors)}";
            var applyFixFailedStep = context.Report.StartStep("ApplyFixChanges");
            context.Report.CompleteStep(applyFixFailedStep, ExecutionTimelineStatus.Failed, message);
            context.Report.SkipStep("DotNetSolutionSyncAfterFix", "Solution sync after fix was skipped because AI fix validation failed.");
            context.Report.SkipStep("BuildValidationAttempt2", "Second build attempt was skipped because AI fix validation failed.");
            context.Report.SkipStep("TestRunAttempt2", "Second test attempt was skipped because AI fix validation failed.");
            context.FailureReason = message;
            return ExecutionStepResult.Continue();
        }

        var applyFixStep = context.Report.StartStep("ApplyFixChanges");
        context.ChangedFilesAfterFix = await _fileChangeApplier.ApplyAsync(
            context.Workspace.RepositoryPath,
            context.AiFixValidationResult.ValidatedChanges,
            cancellationToken);
        context.Report.CompleteStep(applyFixStep, ExecutionTimelineStatus.Succeeded, $"Applied {context.ChangedFilesAfterFix.Count} fix file change(s).");

        if (!context.SolutionBaseline.IsSupported)
        {
            context.Report.SkipStep(
                "DotNetSolutionSyncAfterFix",
                DotNetSolutionSyncDiagnostics.BuildSkippedMessage(context.SolutionBaseline.Message, context.ChangedFilesAfterFix));
        }
        else
        {
            var fixSyncStep = context.Report.StartStep("DotNetSolutionSyncAfterFix");
            var fixSyncResult = await _solutionProjectSynchronizer.SyncAsync(
                context.Workspace.RepositoryPath,
                context.SolutionBaseline,
                cancellationToken);

            if (!fixSyncResult.IsSuccess)
            {
                context.FinalTestStatus = TestExecutionStatus.Failed;
                context.FailureReason = fixSyncResult.Message;
                context.Report.CompleteStep(fixSyncStep, ExecutionTimelineStatus.Failed, fixSyncResult.Message);
                context.Report.SkipStep("BuildValidationAttempt2", "Second build attempt was skipped because solution sync after fix failed.");
                context.Report.SkipStep("TestRunAttempt2", "Second test attempt was skipped because solution sync after fix failed.");
                return ExecutionStepResult.Continue();
            }

            foreach (var project in fixSyncResult.AddedProjects)
            {
                context.AddedProjectsToSolution.Add(project);
            }

            foreach (var project in fixSyncResult.RemovedProjects)
            {
                context.RemovedProjectsFromSolution.Add(project);
            }

            context.Report.CompleteStep(
                fixSyncStep,
                ExecutionTimelineStatus.Succeeded,
                DotNetSolutionSyncDiagnostics.BuildCompletionMessage(
                    fixSyncResult.Message,
                    context.ChangedFilesAfterFix,
                    fixSyncResult));
        }

        context.AiFixSummary = context.AiFixResult!.Summary;
        context.AddWarnings(context.AiFixResult.Warnings);
        context.AddWarnings(context.AiFixValidationResult.Warnings);

        var buildAttempt2Step = context.Report.StartStep("BuildValidationAttempt2");
        var secondBuildResult = await _buildRunner.RunAsync(
            context.Workspace.RepositoryPath,
            context.RepositoryAnalysis!,
            2,
            cancellationToken);
        context.BuildResults.Add(secondBuildResult);
        context.FinalBuildStatus = secondBuildResult.Status;
        context.Report.CompleteStep(
            buildAttempt2Step,
            secondBuildResult.Status is BuildExecutionStatus.Passed or BuildExecutionStatus.Skipped or BuildExecutionStatus.Unsupported
                ? ExecutionTimelineStatus.Succeeded
                : ExecutionTimelineStatus.Failed,
            $"Build attempt 2 finished with status {secondBuildResult.Status}.");
        LogBuildAttempt("BuildValidationAttempt2", secondBuildResult);

        if (secondBuildResult.Status == BuildExecutionStatus.Failed)
        {
            context.FinalTestStatus = TestExecutionStatus.Failed;
            context.FailureReason = $"Build failed after AI fix attempt on attempt {secondBuildResult.AttemptNumber}.";
            context.Report.SkipStep("TestRunAttempt2", "Second test attempt was skipped because build validation failed after the AI fix attempt.");
            return ExecutionStepResult.Continue();
        }

        var testAttempt2Step = context.Report.StartStep("TestRunAttempt2");
        var secondTestResult = await _testRunner.RunAsync(
            context.Workspace.RepositoryPath,
            context.RepositoryAnalysis.TestCommand,
            2,
            cancellationToken);

        context.TestResults.Add(secondTestResult);
        context.FinalTestStatus = secondTestResult.Status;
        context.Report.CompleteStep(
            testAttempt2Step,
            secondTestResult.Status == TestExecutionStatus.Passed ? ExecutionTimelineStatus.Succeeded : ExecutionTimelineStatus.Failed,
            $"Test attempt 2 finished with status {secondTestResult.Status}.");
        LogTestAttempt("TestRunAttempt2", secondTestResult, string.Empty);

        if (secondTestResult.Status != TestExecutionStatus.Passed)
        {
            context.FailureReason = $"Tests failed after AI fix attempt on attempt {secondTestResult.AttemptNumber}.";
        }

        return ExecutionStepResult.Continue();
    }

    private void LogBuildAttempt(string step, BuildResult buildResult)
    {
        _logger.LogInformation(
            "Execution step {Step} completed with buildStatus={BuildStatus}; exitCode={ExitCode}; attemptNumber={AttemptNumber}; buildCommand={BuildCommand}; stdoutFirstLines={StdoutFirstLines}; stderrFirstLines={StderrFirstLines}",
            step,
            buildResult.Status,
            buildResult.ExitCode,
            buildResult.AttemptNumber,
            buildResult.Command,
            ExecutionOutputLogFormatter.BuildFirstLinesSummary(buildResult.Stdout),
            ExecutionOutputLogFormatter.BuildFirstLinesSummary(buildResult.Stderr));
    }

    private void LogTestAttempt(string step, TestResult testResult, string reason)
    {
        _logger.LogInformation(
            "Execution step {Step} completed with testStatus={TestStatus}; exitCode={ExitCode}; attemptNumber={AttemptNumber}; testCommand={TestCommand}; reason={Reason}; stdoutFirstLines={StdoutFirstLines}; stderrFirstLines={StderrFirstLines}",
            step,
            testResult.Status,
            testResult.ExitCode,
            testResult.AttemptNumber,
            testResult.Command,
            reason,
            ExecutionOutputLogFormatter.BuildFirstLinesSummary(testResult.Stdout),
            ExecutionOutputLogFormatter.BuildFirstLinesSummary(testResult.Stderr));
    }
}

internal static class DotNetSolutionSyncDiagnostics
{
    public static string BuildSkippedMessage(string baselineMessage, IReadOnlyList<string> changedFiles)
    {
        if (!ContainsProjectFileChange(changedFiles))
        {
            return baselineMessage;
        }

        return $"{baselineMessage} .csproj changes were applied without automatic solution membership updates because no supported solution baseline was available.";
    }

    public static string BuildCompletionMessage(
        string syncMessage,
        IReadOnlyList<string> changedFiles,
        DotNetSolutionSyncResult syncResult)
    {
        if (!ContainsProjectFileChange(changedFiles))
        {
            return syncMessage;
        }

        if (syncResult.AddedProjects.Count > 0 || syncResult.RemovedProjects.Count > 0)
        {
            return syncMessage;
        }

        return $"{syncMessage} .csproj changes were detected, but no solution membership changes were needed. If a new project was expected to be added, verify that the .csproj was newly created inside the repository and was not already part of the captured solution baseline.";
    }

    private static bool ContainsProjectFileChange(IReadOnlyList<string> changedFiles)
    {
        return changedFiles.Any(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed class GitFinalizationStep : IExecutionStep
{
    private readonly IGitClient _gitClient;
    private readonly IBranchNameBuilder _branchNameBuilder;
    private readonly IPrDescriptionBuilder _prDescriptionBuilder;
    private readonly IPullRequestService _pullRequestService;
    private readonly ICanRunGitFinalizationRule _gitFinalizationRule;

    public GitFinalizationStep(
        IGitClient gitClient,
        IBranchNameBuilder branchNameBuilder,
        IPrDescriptionBuilder prDescriptionBuilder,
        IPullRequestService pullRequestService,
        ICanRunGitFinalizationRule gitFinalizationRule)
    {
        _gitClient = gitClient;
        _branchNameBuilder = branchNameBuilder;
        _prDescriptionBuilder = prDescriptionBuilder;
        _pullRequestService = pullRequestService;
        _gitFinalizationRule = gitFinalizationRule;
    }

    public int Order => 100;

    public async Task<ExecutionStepResult> ExecuteAsync(ExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!_gitFinalizationRule.CanRun(context))
        {
            context.Report.SkipStep("GitDiffSummary", $"Git diff summary skipped because final test status was {context.FinalTestStatus}.");
            context.Report.SkipStep("GitCreateBranch", $"Branch creation skipped because final test status was {context.FinalTestStatus}.");
            context.Report.SkipStep("GitCommit", $"Commit skipped because final test status was {context.FinalTestStatus}.");
            context.Report.SkipStep("GitPush", $"Push skipped because final test status was {context.FinalTestStatus}.");
            context.Report.SkipStep("PullRequestCreateOrGet", $"Pull request creation skipped because final test status was {context.FinalTestStatus}.");
            return ExecutionStepResult.Continue();
        }

        var diffStep = context.Report.StartStep("GitDiffSummary");
        var hasChanges = await _gitClient.HasChangesAsync(context.Workspace!.RepositoryPath, cancellationToken);
        if (!hasChanges)
        {
            var message = "No repository changes detected after AI modifications.";
            context.GitFinalizationStatus = GitFinalizationStatus.Skipped;
            context.Report.CompleteStep(diffStep, ExecutionTimelineStatus.Failed, message);
            context.Report.SkipStep("GitCreateBranch", "Branch creation skipped because no repository changes were detected.");
            context.Report.SkipStep("GitCommit", "Commit skipped because no repository changes were detected.");
            context.Report.SkipStep("GitPush", "Push skipped because no repository changes were detected.");
            context.Report.SkipStep("PullRequestCreateOrGet", "Pull request creation skipped because no repository changes were detected.");
            context.MarkFailure(message);
            return ExecutionStepResult.Stop();
        }

        context.DiffSummary = await _gitClient.GetDiffSummaryAsync(context.Workspace.RepositoryPath, cancellationToken);
        context.Report.CompleteStep(diffStep, ExecutionTimelineStatus.Succeeded, "Git diff summary collected.");

        context.BranchName = _branchNameBuilder.Build(context.Request.TaskId, context.Request.Title);
        var createBranchStep = context.Report.StartStep("GitCreateBranch");
        var createBranchResult = await _gitClient.CreateBranchAsync(
            context.Workspace.RepositoryPath,
            context.BranchName,
            cancellationToken);

        if (!createBranchResult.IsSuccess)
        {
            context.GitFinalizationStatus = GitFinalizationStatus.Failed;
            context.Report.CompleteStep(createBranchStep, ExecutionTimelineStatus.Failed, createBranchResult.Message);
            context.Report.SkipStep("GitCommit", "Commit skipped because branch creation failed.");
            context.Report.SkipStep("GitPush", "Push skipped because branch creation failed.");
            context.Report.SkipStep("PullRequestCreateOrGet", "Pull request creation skipped because branch creation failed.");
            context.MarkFailure(createBranchResult.Message);
            return ExecutionStepResult.Stop();
        }

        context.Report.CompleteStep(createBranchStep, ExecutionTimelineStatus.Succeeded, $"Branch '{context.BranchName}' created.");
        context.CommitMessage = $"{context.Request.TaskId} {context.Request.Title}".Trim();

        var commitStep = context.Report.StartStep("GitCommit");
        var commitResult = await _gitClient.CommitAsync(
            context.Workspace.RepositoryPath,
            context.CommitMessage,
            cancellationToken);

        if (!commitResult.IsSuccess)
        {
            context.GitFinalizationStatus = GitFinalizationStatus.Failed;
            context.Report.CompleteStep(commitStep, ExecutionTimelineStatus.Failed, commitResult.Message);
            context.Report.SkipStep("GitPush", "Push skipped because commit failed.");
            context.Report.SkipStep("PullRequestCreateOrGet", "Pull request creation skipped because commit failed.");
            context.MarkFailure(commitResult.Message);
            return ExecutionStepResult.Stop();
        }

        context.GitChangedFiles = (await _gitClient.GetCommittedFilesAsync(context.Workspace.RepositoryPath, cancellationToken)).ToArray();
        context.Report.CompleteStep(commitStep, ExecutionTimelineStatus.Succeeded, $"Commit '{context.CommitMessage}' created.");

        var pushStep = context.Report.StartStep("GitPush");
        var pushResult = await _gitClient.PushAsync(
            context.Workspace.RepositoryPath,
            context.BranchName,
            cancellationToken);

        if (!pushResult.IsSuccess)
        {
            context.GitFinalizationStatus = GitFinalizationStatus.Failed;
            context.Report.CompleteStep(pushStep, ExecutionTimelineStatus.Failed, pushResult.Message);
            context.Report.SkipStep("PullRequestCreateOrGet", "Pull request creation skipped because push failed.");
            context.MarkFailure(pushResult.Message);
            return ExecutionStepResult.Stop();
        }

        context.Pushed = true;
        context.Report.CompleteStep(pushStep, ExecutionTimelineStatus.Succeeded, $"Branch '{context.BranchName}' pushed successfully.");

        var interimReport = context.Report.Build(context);
        var prBody = _prDescriptionBuilder.Build(interimReport);
        var prStep = context.Report.StartStep("PullRequestCreateOrGet");
        var prResult = await _pullRequestService.CreateOrGetPullRequestAsync(
            context.Request.ParsedTask.RepositoryUrl,
            context.Request.ParsedTask.BaseBranch,
            context.BranchName,
            $"{context.Request.TaskId} {context.Request.Title}".Trim(),
            prBody,
            cancellationToken);

        context.PullRequestUrl = prResult.PullRequestUrl;
        context.PullRequestNumber = prResult.PullRequestNumber;
        context.PullRequestStatus = prResult.Status;
        context.GitFinalizationStatus = prResult.Status == PullRequestStatus.AlreadyExists
            ? GitFinalizationStatus.AlreadyExists
            : GitFinalizationStatus.Completed;

        context.Report.CompleteStep(
            prStep,
            ExecutionTimelineStatus.Succeeded,
            prResult.Status == PullRequestStatus.AlreadyExists
                ? $"Existing pull request reused: {context.PullRequestUrl}"
                : $"Pull request created: {context.PullRequestUrl}");

        return ExecutionStepResult.Continue();
    }
}
