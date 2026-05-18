namespace AiAgentChallenge.Domain;

public sealed class ExecutionReport
{
    public Guid ExecutionId { get; init; }

    public string TaskId { get; init; } = string.Empty;

    public ExecutionStatus Status { get; init; }

    public string TraceId { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public string Message { get; init; } = string.Empty;

    public IReadOnlyList<ExecutionTimelineEntry> Timeline { get; init; } = Array.Empty<ExecutionTimelineEntry>();

    public ParsedTask? ParsedTask { get; init; }

    public string WorkspacePath { get; init; } = string.Empty;

    public string RepositoryPath { get; init; } = string.Empty;

    public CloneStatus CloneStatus { get; init; }

    public RepositoryAnalysis? RepositoryAnalysis { get; init; }

    public string SolutionFile { get; init; } = string.Empty;

    public string AiModel { get; init; } = string.Empty;

    public string AiSummary { get; init; } = string.Empty;

    public IReadOnlyList<string> ChangedFiles { get; init; } = Array.Empty<string>();

    public string AiTestNotes { get; init; } = string.Empty;

    public IReadOnlyList<string> AiWarnings { get; init; } = Array.Empty<string>();

    public AiUsageInfo? AiUsage { get; init; }

    public IReadOnlyList<BuildResult> BuildResults { get; init; } = Array.Empty<BuildResult>();

    public BuildExecutionStatus FinalBuildStatus { get; init; } = BuildExecutionStatus.Skipped;

    public IReadOnlyList<TestResult> TestResults { get; init; } = Array.Empty<TestResult>();

    public TestExecutionStatus FinalTestStatus { get; init; }

    public bool AiFixAttempted { get; init; }

    public string AiFixSummary { get; init; } = string.Empty;

    public IReadOnlyList<string> ChangedFilesAfterFix { get; init; } = Array.Empty<string>();

    public string FailureReason { get; init; } = string.Empty;

    public string BranchName { get; init; } = string.Empty;

    public string CommitMessage { get; init; } = string.Empty;

    public IReadOnlyList<string> AddedProjectsToSolution { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RemovedProjectsFromSolution { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> GitChangedFiles { get; init; } = Array.Empty<string>();

    public string DiffSummary { get; init; } = string.Empty;

    public IReadOnlyList<string> DiffSummaryLines { get; init; } = Array.Empty<string>();

    public bool Pushed { get; init; }

    public string PullRequestUrl { get; init; } = string.Empty;

    public int PullRequestNumber { get; init; }

    public PullRequestStatus? PullRequestStatus { get; init; }

    public GitFinalizationStatus GitFinalizationStatus { get; init; }
}
