using AiAgentChallenge.Application.Tasks;
using AiAgentChallenge.Domain;
using AiAgentChallenge.Infrastructure.Text;

namespace AiAgentChallenge.Infrastructure.Orchestration;

public interface IExecutionContextFactory
{
    ExecutionContext Create(CreateTaskExecutionRequest request);
}

public interface IExecutionPipelineFactory
{
    IReadOnlyList<IExecutionStep> Create();
}

public interface IExecutionStep
{
    int Order { get; }

    Task<ExecutionStepResult> ExecuteAsync(
        ExecutionContext context,
        CancellationToken cancellationToken = default);
}

public sealed class ExecutionStepResult
{
    private ExecutionStepResult(bool shouldContinue)
    {
        ShouldContinue = shouldContinue;
    }

    public bool ShouldContinue { get; }

    public static ExecutionStepResult Continue() => new(true);

    public static ExecutionStepResult Stop() => new(false);
}

public sealed class ExecutionPipelineFactory : IExecutionPipelineFactory
{
    private readonly IReadOnlyList<IExecutionStep> _steps;

    public ExecutionPipelineFactory(IEnumerable<IExecutionStep> steps)
    {
        _steps = steps
            .OrderBy(step => step.Order)
            .ToArray();
    }

    public IReadOnlyList<IExecutionStep> Create() => _steps;
}

public sealed class ExecutionContextFactory : IExecutionContextFactory
{
    public ExecutionContext Create(CreateTaskExecutionRequest request)
    {
        var traceId = string.IsNullOrWhiteSpace(request.TraceId)
            ? Guid.NewGuid().ToString("N")
            : request.TraceId;
        var executionId = request.ExecutionId == Guid.Empty
            ? Guid.NewGuid()
            : request.ExecutionId;

        return new ExecutionContext(
            request,
            executionId,
            DateTimeOffset.UtcNow,
            traceId,
            new ExecutionReportAccumulator(request, traceId));
    }
}

public sealed class ExecutionContext
{
    public ExecutionContext(
        CreateTaskExecutionRequest request,
        Guid executionId,
        DateTimeOffset createdAtUtc,
        string traceId,
        ExecutionReportAccumulator report)
    {
        Request = request;
        ExecutionId = executionId;
        CreatedAtUtc = createdAtUtc;
        TraceId = traceId;
        Report = report;
    }

    public CreateTaskExecutionRequest Request { get; }

    public Guid ExecutionId { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public string TraceId { get; }

    public ExecutionReportAccumulator Report { get; }

    public ExecutionStatus Status { get; set; } = ExecutionStatus.Queued;

    public string Message { get; set; } = string.Empty;

    public string FailureReason { get; set; } = string.Empty;

    public bool StopProcessing { get; private set; }

    public CloneStatus CloneStatus { get; set; } = CloneStatus.NotStarted;

    public WorkspaceInfo? Workspace { get; set; }

    public RepositoryAnalysis? RepositoryAnalysis { get; set; }

    public DotNetSolutionBaseline SolutionBaseline { get; set; } =
        DotNetSolutionBaseline.Unsupported("Solution sync has not been evaluated.");

    public AgentContext? AgentContext { get; set; }

    public AiCodeChangeResult? AiCodeChangeResult { get; set; }

    public AiCodeChangeResult? AiFixResult { get; set; }

    public AiChangeValidationResult? ValidationResult { get; set; }

    public AiChangeValidationResult? AiFixValidationResult { get; set; }

    public bool FormattingRegenerated { get; set; }

    public IReadOnlyList<string> ChangedFiles { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> ChangedFilesAfterFix { get; set; } = Array.Empty<string>();

    public List<AiChangeWarning> ProviderWarnings { get; } = new();

    public List<string> AiWarnings { get; } = new();

    public List<TestResult> TestResults { get; } = new();

    public List<BuildResult> BuildResults { get; } = new();

    public BuildExecutionStatus FinalBuildStatus { get; set; } = BuildExecutionStatus.Skipped;

    public TestExecutionStatus FinalTestStatus { get; set; } = TestExecutionStatus.Skipped;

    public bool AiFixAttempted { get; set; }

    public string AiFixSummary { get; set; } = string.Empty;

    public HashSet<string> AddedProjectsToSolution { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> RemovedProjectsFromSolution { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string BranchName { get; set; } = string.Empty;

    public string CommitMessage { get; set; } = string.Empty;

    public IReadOnlyList<string> GitChangedFiles { get; set; } = Array.Empty<string>();

    public string DiffSummary { get; set; } = string.Empty;

    public bool Pushed { get; set; }

    public string PullRequestUrl { get; set; } = string.Empty;

    public int PullRequestNumber { get; set; }

    public PullRequestStatus? PullRequestStatus { get; set; }

    public GitFinalizationStatus GitFinalizationStatus { get; set; } = GitFinalizationStatus.Skipped;

    public void AddWarnings(IEnumerable<AiChangeWarning> warnings)
    {
        foreach (var warning in warnings)
        {
            ProviderWarnings.Add(warning);
            AiWarnings.Add(string.IsNullOrWhiteSpace(warning.Path)
                ? warning.Message
                : $"{warning.Path}: {warning.Message}");
        }
    }

    public void AddWarningMessages(IEnumerable<string> warnings)
    {
        AiWarnings.AddRange(warnings);
    }

    public void MarkFailure(string message, string? failureReason = null)
    {
        Status = ExecutionStatus.Failed;
        Message = message;
        FailureReason = string.IsNullOrWhiteSpace(failureReason) ? message : failureReason;
        StopProcessing = true;
    }

    public void FinalizeOutcome()
    {
        if (Status == ExecutionStatus.Failed)
        {
            if (string.IsNullOrWhiteSpace(Message))
            {
                Message = string.IsNullOrWhiteSpace(FailureReason)
                    ? "Task execution failed."
                    : FailureReason;
            }

            return;
        }

        if (FinalBuildStatus == BuildExecutionStatus.Failed)
        {
            Status = ExecutionStatus.Failed;
            FailureReason = string.IsNullOrWhiteSpace(FailureReason)
                ? "Final build validation failed."
                : FailureReason;
            Message = "Task execution stopped after build validation failed.";
            return;
        }

        if (FinalTestStatus is TestExecutionStatus.Passed or TestExecutionStatus.Skipped)
        {
            Status = ExecutionStatus.Completed;
            Message = "Task execution completed through parsing, repository preparation, AI change generation, validation, build verification, test execution, and GitHub pull request finalization.";
            return;
        }

        Status = ExecutionStatus.Failed;
        FailureReason = string.IsNullOrWhiteSpace(FailureReason)
            ? $"Final test status was {FinalTestStatus}."
            : FailureReason;
        Message = "Task execution stopped after test validation failed.";
    }
}

public sealed class ExecutionReportAccumulator
{
    private readonly CreateTaskExecutionRequest _request;
    private readonly string _traceId;
    private readonly List<ExecutionTimelineEntry> _timeline = new();

    public ExecutionReportAccumulator(CreateTaskExecutionRequest request, string traceId)
    {
        _request = request;
        _traceId = traceId;
    }

    public ExecutionTimelineEntry StartStep(string step, string message = "")
    {
        var entry = new ExecutionTimelineEntry
        {
            Step = step,
            Status = ExecutionTimelineStatus.Started,
            StartedAtUtc = DateTimeOffset.UtcNow,
            Message = message
        };

        _timeline.Add(entry);
        return entry;
    }

    public void CompleteStep(ExecutionTimelineEntry entry, ExecutionTimelineStatus status, string message = "")
    {
        var finishedAtUtc = DateTimeOffset.UtcNow;
        var index = _timeline.FindIndex(item => ReferenceEquals(item, entry));
        if (index < 0)
        {
            return;
        }

        _timeline[index] = new ExecutionTimelineEntry
        {
            Step = entry.Step,
            Status = status,
            StartedAtUtc = entry.StartedAtUtc,
            FinishedAtUtc = finishedAtUtc,
            DurationMs = Math.Max(0, (long)(finishedAtUtc - entry.StartedAtUtc).TotalMilliseconds),
            Message = string.IsNullOrWhiteSpace(message) ? entry.Message : message
        };
    }

    public void SkipStep(string step, string message)
    {
        var now = DateTimeOffset.UtcNow;
        _timeline.Add(new ExecutionTimelineEntry
        {
            Step = step,
            Status = ExecutionTimelineStatus.Skipped,
            StartedAtUtc = now,
            FinishedAtUtc = now,
            DurationMs = 0,
            Message = message
        });
    }

    public ExecutionReport Build(ExecutionContext context)
    {
        var changedFilesAfterFix = NormalizeFileList(context.ChangedFilesAfterFix);
        var changedFiles = NormalizeFileList(context.ChangedFiles.Concat(changedFilesAfterFix));
        var gitChangedFiles = NormalizeFileList(context.GitChangedFiles);

        return new ExecutionReport
        {
            ExecutionId = context.ExecutionId,
            TaskId = _request.TaskId,
            Status = context.Status,
            TraceId = _traceId,
            CreatedAtUtc = context.CreatedAtUtc,
            Message = context.Message,
            Timeline = _timeline.ToArray(),
            ParsedTask = BuildParsedTask(),
            WorkspacePath = context.Workspace?.WorkspacePath ?? string.Empty,
            RepositoryPath = context.Workspace?.RepositoryPath ?? string.Empty,
            CloneStatus = context.CloneStatus,
            RepositoryAnalysis = context.RepositoryAnalysis,
            SolutionFile = context.SolutionBaseline.SolutionPath,
            AiModel = context.AiCodeChangeResult?.Usage?.Model ?? string.Empty,
            AiSummary = context.AiCodeChangeResult?.Summary ?? string.Empty,
            ChangedFiles = changedFiles,
            AiTestNotes = context.AiCodeChangeResult?.TestNotes ?? string.Empty,
            AiWarnings = context.AiWarnings
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            AiUsage = context.AiCodeChangeResult?.Usage,
            BuildResults = context.BuildResults.ToArray(),
            FinalBuildStatus = context.FinalBuildStatus,
            TestResults = context.TestResults.ToArray(),
            FinalTestStatus = context.FinalTestStatus,
            AiFixAttempted = context.AiFixAttempted,
            AiFixSummary = context.AiFixSummary,
            ChangedFilesAfterFix = changedFilesAfterFix,
            FailureReason = context.FailureReason,
            BranchName = context.BranchName,
            CommitMessage = context.CommitMessage,
            AddedProjectsToSolution = context.AddedProjectsToSolution.ToArray(),
            RemovedProjectsFromSolution = context.RemovedProjectsFromSolution.ToArray(),
            GitChangedFiles = gitChangedFiles,
            DiffSummary = context.DiffSummary,
            DiffSummaryLines = ReportTextFormatter.ToLines(context.DiffSummary),
            Pushed = context.Pushed,
            PullRequestUrl = context.PullRequestUrl,
            PullRequestNumber = context.PullRequestNumber,
            PullRequestStatus = context.PullRequestStatus,
            GitFinalizationStatus = context.GitFinalizationStatus
        };
    }

    private ParsedTask BuildParsedTask()
    {
        return new ParsedTask
        {
            TaskId = _request.TaskId,
            RepositoryUrl = _request.ParsedTask.RepositoryUrl,
            BaseBranch = _request.ParsedTask.BaseBranch,
            Requirement = _request.ParsedTask.Requirement,
            AcceptanceCriteria = _request.ParsedTask.AcceptanceCriteria
        };
    }

    private static IReadOnlyList<string> NormalizeFileList(IEnumerable<string> files)
    {
        return files
            .Where(static file => !string.IsNullOrWhiteSpace(file))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
