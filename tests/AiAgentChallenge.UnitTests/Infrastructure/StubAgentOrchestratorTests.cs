using AiAgentChallenge.Application.Tasks;
using AiAgentChallenge.Domain;
using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Infrastructure.Ai;
using AiAgentChallenge.Infrastructure.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class StubAgentOrchestratorTests
{
    [Fact]
    public async Task StartAsync_ReturnsCompletedExecutionReport()
    {
        var parsedTask = new ParsedTask
        {
            RepositoryUrl = "https://github.com/example-company/user-service",
            BaseBranch = "main",
            Requirement = "Implement email validation on the registration endpoint.",
            AcceptanceCriteria = Array.Empty<string>()
        };
        var workspace = new WorkspaceInfo
        {
            SafeTaskId = "TASK-123",
            RunId = "run-1",
            WorkspacePath = @"C:\temp\TASK-123\run-1",
            RepositoryPath = @"C:\temp\TASK-123\run-1\source"
        };
        var orchestrator = new StubAgentOrchestrator(
            new FakeRepositoryPolicy(RepositoryPolicyResult.Success("example-company")),
            new FakeWorkspaceService(workspace),
            new FakeGitClient(GitCloneResult.Success(0, "ok", string.Empty)),
            new FakeRepositoryAnalyzer(new RepositoryAnalysis
            {
                Language = "C#",
                Framework = "ASP.NET Core",
                BuildTool = "dotnet",
                TestCommand = "dotnet test"
            }),
            new FakeAgentContextBuilder(new AgentContext
            {
                TaskSummary = "Requirement: Implement email validation",
                RepositoryAnalysisSummary = "Language: C#"
            }),
            new FakeAiCodeAgent(new AiCodeChangeResult
            {
                Summary = "Added validation",
                ChangedFiles = new[]
                {
                    new AiChangedFile
                    {
                        Path = "src/Users/RegisterService.cs",
                        Operation = "modify",
                        Content = "public class RegisterService {}"
                    }
                },
                TestNotes = "Add unit tests",
                Usage = new AiUsageInfo
                {
                    Model = "gpt-4.1-mini"
                }
            }),
            new FakeAiChangeValidator(AiChangeValidationResult.Success(
                new[]
                {
                    new AiChangedFile
                    {
                        Path = "src/Users/RegisterService.cs",
                        Operation = "modify",
                        Content = "public class RegisterService {}"
                    }
                },
                Array.Empty<AiChangeWarning>())),
            new FakeFileChangeApplier(new[] { "src/Users/RegisterService.cs" }),
            new FakeTestRunner(new[]
            {
                new TestResult
                {
                    Command = "dotnet test",
                    Status = TestExecutionStatus.Passed,
                    ExitCode = 0,
                    Duration = TimeSpan.FromSeconds(1),
                    Stdout = "restore\r\n\r\npassed\r\n",
                    StdoutLines = new[] { "restore", string.Empty, "passed" },
                    AttemptNumber = 1
                }
            }),
            new FakeBranchNameBuilder("ai-agent/TASK-123-add-email-validation"),
            new FakePrDescriptionBuilder("PR body"),
            new FakePullRequestService(new PullRequestResult
            {
                PullRequestUrl = "https://github.com/example-company/user-service/pull/42",
                PullRequestNumber = 42,
                Status = PullRequestStatus.Created
            }),
            Options.Create(new AiOptions()),
            NullLogger<StubAgentOrchestrator>.Instance);

        var request = new CreateTaskExecutionRequest
        {
            TaskId = "TASK-123",
            TraceId = "trace-123",
            Title = "Add email validation",
            Description = "Implement email validation on the registration endpoint.",
            ParsedTask = parsedTask
        };

        var report = await orchestrator.StartAsync(request);

        Assert.NotEqual(Guid.Empty, report.ExecutionId);
        Assert.Equal(request.TaskId, report.TaskId);
        Assert.Equal("trace-123", report.TraceId);
        Assert.Equal(ExecutionStatus.Completed, report.Status);
        Assert.False(string.IsNullOrWhiteSpace(report.Message));
        Assert.True(report.CreatedAtUtc <= DateTimeOffset.UtcNow);
        Assert.NotNull(report.ParsedTask);
        Assert.Equal(request.TaskId, report.ParsedTask!.TaskId);
        Assert.Equal(parsedTask.RepositoryUrl, report.ParsedTask.RepositoryUrl);
        Assert.Equal(parsedTask.BaseBranch, report.ParsedTask.BaseBranch);
        Assert.Equal(parsedTask.Requirement, report.ParsedTask.Requirement);
        Assert.Equal(CloneStatus.Succeeded, report.CloneStatus);
        Assert.Equal(workspace.WorkspacePath, report.WorkspacePath);
        Assert.Equal(workspace.RepositoryPath, report.RepositoryPath);
        Assert.NotNull(report.RepositoryAnalysis);
        Assert.Equal("C#", report.RepositoryAnalysis!.Language);
        Assert.Equal("gpt-4.1-mini", report.AiModel);
        Assert.Equal("Added validation", report.AiSummary);
        Assert.Contains("src/Users/RegisterService.cs", report.ChangedFiles);
        Assert.Single(report.TestResults);
        Assert.Equal(new[] { "restore", string.Empty, "passed" }, report.TestResults[0].StdoutLines);
        Assert.Equal(TestExecutionStatus.Passed, report.FinalTestStatus);
        Assert.Equal("ai-agent/TASK-123-add-email-validation", report.BranchName);
        Assert.Equal("TASK-123 Add email validation", report.CommitMessage);
        Assert.Contains("src/File.cs", report.GitChangedFiles);
        Assert.True(report.Pushed);
        Assert.Equal("https://github.com/example-company/user-service/pull/42", report.PullRequestUrl);
        Assert.Equal(42, report.PullRequestNumber);
        Assert.Equal(PullRequestStatus.Created, report.PullRequestStatus);
        Assert.Equal(GitFinalizationStatus.Completed, report.GitFinalizationStatus);
        Assert.Equal("src/File.cs | 4 ++--", report.DiffSummary);
        Assert.Equal(new[] { "src/File.cs | 4 ++--" }, report.DiffSummaryLines);
        Assert.Contains(report.Timeline, entry => entry.Step == "RepositoryPolicyValidation" && entry.Status == ExecutionTimelineStatus.Succeeded);
        Assert.Contains(report.Timeline, entry => entry.Step == "RepositoryClone" && entry.Status == ExecutionTimelineStatus.Succeeded);
        Assert.Contains(report.Timeline, entry => entry.Step == "TestRunAttempt1" && entry.Status == ExecutionTimelineStatus.Succeeded);
        Assert.Contains(report.Timeline, entry => entry.Step == "PullRequestCreateOrGet" && entry.Status == ExecutionTimelineStatus.Succeeded);
        Assert.All(report.Timeline.Where(entry => entry.Status != ExecutionTimelineStatus.Started), entry =>
        {
            Assert.NotNull(entry.FinishedAtUtc);
            Assert.True(entry.DurationMs >= 0);
        });
    }

    [Fact]
    public async Task StartAsync_ReturnsFailedReport_WhenPolicyValidationFails()
    {
        var orchestrator = new StubAgentOrchestrator(
            new FakeRepositoryPolicy(
                RepositoryPolicyResult.Failure(
                    RepositoryPolicyErrorCode.DisallowedHost,
                    "Repository host 'gitlab.com' is not allowed.")),
            new FakeWorkspaceService(new WorkspaceInfo()),
            new FakeGitClient(GitCloneResult.Success(0, "ok", string.Empty)),
            new FakeRepositoryAnalyzer(new RepositoryAnalysis()),
            new FakeAgentContextBuilder(new AgentContext()),
            new FakeAiCodeAgent(new AiCodeChangeResult()),
            new FakeAiChangeValidator(AiChangeValidationResult.Success(Array.Empty<AiChangedFile>(), Array.Empty<AiChangeWarning>())),
            new FakeFileChangeApplier(Array.Empty<string>()),
            new FakeTestRunner(Array.Empty<TestResult>()),
            new FakeBranchNameBuilder("ai-agent/TASK-123"),
            new FakePrDescriptionBuilder("PR body"),
            new FakePullRequestService(new PullRequestResult()),
            Options.Create(new AiOptions()),
            NullLogger<StubAgentOrchestrator>.Instance);

        var request = new CreateTaskExecutionRequest
        {
            TaskId = "TASK-123",
            ParsedTask = new ParsedTask
            {
                RepositoryUrl = "https://gitlab.com/example-company/user-service",
                BaseBranch = "main",
                Requirement = "Implement email validation"
            }
        };

        var report = await orchestrator.StartAsync(request);

        Assert.Equal(ExecutionStatus.Failed, report.Status);
        Assert.False(string.IsNullOrWhiteSpace(report.TraceId));
        Assert.Equal(CloneStatus.Failed, report.CloneStatus);
        Assert.Contains("not allowed", report.Message);
        Assert.Contains(report.Timeline, entry => entry.Step == "RepositoryPolicyValidation" && entry.Status == ExecutionTimelineStatus.Failed);
    }

    [Fact]
    public async Task StartAsync_ReturnsFailedReport_WhenCloneFails()
    {
        var workspace = new WorkspaceInfo
        {
            WorkspacePath = @"C:\temp\TASK-123\run-1",
            RepositoryPath = @"C:\temp\TASK-123\run-1\source"
        };

        var orchestrator = new StubAgentOrchestrator(
            new FakeRepositoryPolicy(RepositoryPolicyResult.Success("example-company")),
            new FakeWorkspaceService(workspace),
            new FakeGitClient(GitCloneResult.Failure(
                GitCloneErrorCode.BranchNotFound,
                "The requested base branch was not found in the remote repository.",
                1,
                string.Empty,
                "fatal")),
            new FakeRepositoryAnalyzer(new RepositoryAnalysis()),
            new FakeAgentContextBuilder(new AgentContext()),
            new FakeAiCodeAgent(new AiCodeChangeResult()),
            new FakeAiChangeValidator(AiChangeValidationResult.Success(Array.Empty<AiChangedFile>(), Array.Empty<AiChangeWarning>())),
            new FakeFileChangeApplier(Array.Empty<string>()),
            new FakeTestRunner(Array.Empty<TestResult>()),
            new FakeBranchNameBuilder("ai-agent/TASK-123"),
            new FakePrDescriptionBuilder("PR body"),
            new FakePullRequestService(new PullRequestResult()),
            Options.Create(new AiOptions()),
            NullLogger<StubAgentOrchestrator>.Instance);

        var report = await orchestrator.StartAsync(new CreateTaskExecutionRequest
        {
            TaskId = "TASK-123",
            ParsedTask = new ParsedTask
            {
                RepositoryUrl = "https://github.com/example-company/user-service",
                BaseBranch = "missing-branch",
                Requirement = "Implement email validation"
            }
        });

        Assert.Equal(ExecutionStatus.Failed, report.Status);
        Assert.Equal(CloneStatus.Failed, report.CloneStatus);
        Assert.Contains(report.Timeline, entry => entry.Step == "RepositoryClone" && entry.Status == ExecutionTimelineStatus.Failed);
    }

    [Fact]
    public async Task StartAsync_ReturnsFailedReport_WhenAnalysisFails()
    {
        var workspace = new WorkspaceInfo
        {
            WorkspacePath = @"C:\temp\TASK-123\run-1",
            RepositoryPath = @"C:\temp\TASK-123\run-1\source"
        };

        var orchestrator = new StubAgentOrchestrator(
            new FakeRepositoryPolicy(RepositoryPolicyResult.Success("example-company")),
            new FakeWorkspaceService(workspace),
            new FakeGitClient(GitCloneResult.Success(0, "ok", string.Empty)),
            new ThrowingRepositoryAnalyzer(),
            new FakeAgentContextBuilder(new AgentContext()),
            new FakeAiCodeAgent(new AiCodeChangeResult()),
            new FakeAiChangeValidator(AiChangeValidationResult.Success(Array.Empty<AiChangedFile>(), Array.Empty<AiChangeWarning>())),
            new FakeFileChangeApplier(Array.Empty<string>()),
            new FakeTestRunner(Array.Empty<TestResult>()),
            new FakeBranchNameBuilder("ai-agent/TASK-123"),
            new FakePrDescriptionBuilder("PR body"),
            new FakePullRequestService(new PullRequestResult()),
            Options.Create(new AiOptions()),
            NullLogger<StubAgentOrchestrator>.Instance);

        var request = new CreateTaskExecutionRequest
        {
            TaskId = "TASK-123",
            ParsedTask = new ParsedTask
            {
                RepositoryUrl = "https://github.com/example-company/user-service",
                BaseBranch = "main",
                Requirement = "Implement email validation"
            }
        };

        var report = await orchestrator.StartAsync(request);

        Assert.Equal(ExecutionStatus.Failed, report.Status);
        Assert.Equal(CloneStatus.Succeeded, report.CloneStatus);
        Assert.Contains("Repository analysis failed", report.Message);
    }

    [Fact]
    public async Task StartAsync_ReturnsFailedReport_WhenAiValidationFails()
    {
        var workspace = new WorkspaceInfo
        {
            WorkspacePath = @"C:\temp\TASK-123\run-1",
            RepositoryPath = @"C:\temp\TASK-123\run-1\source"
        };

        var orchestrator = new StubAgentOrchestrator(
            new FakeRepositoryPolicy(RepositoryPolicyResult.Success("example-company")),
            new FakeWorkspaceService(workspace),
            new FakeGitClient(GitCloneResult.Success(0, "ok", string.Empty)),
            new FakeRepositoryAnalyzer(new RepositoryAnalysis
            {
                Language = "C#"
            }),
            new FakeAgentContextBuilder(new AgentContext()),
            new FakeAiCodeAgent(new AiCodeChangeResult
            {
                Summary = "Summary",
                ChangedFiles = new[]
                {
                    new AiChangedFile
                    {
                        Path = "src/File.cs",
                        Operation = "modify",
                        Content = "content"
                    }
                },
                TestNotes = "notes"
            }),
            new FakeAiChangeValidator(AiChangeValidationResult.Failure("absolute path rejected")),
            new FakeFileChangeApplier(Array.Empty<string>()),
            new FakeTestRunner(Array.Empty<TestResult>()),
            new FakeBranchNameBuilder("ai-agent/TASK-123"),
            new FakePrDescriptionBuilder("PR body"),
            new FakePullRequestService(new PullRequestResult()),
            Options.Create(new AiOptions()),
            NullLogger<StubAgentOrchestrator>.Instance);

        var request = new CreateTaskExecutionRequest
        {
            TaskId = "TASK-123",
            ParsedTask = new ParsedTask
            {
                RepositoryUrl = "https://github.com/example-company/user-service",
                BaseBranch = "main",
                Requirement = "Implement email validation"
            }
        };

        var report = await orchestrator.StartAsync(request);

        Assert.Equal(ExecutionStatus.Failed, report.Status);
        Assert.Contains("AI change validation failed", report.Message);
    }

    [Fact]
    public async Task StartAsync_WhenAiOutputIsCollapsed_RegeneratesFormattingAndContinues()
    {
        var workspace = new WorkspaceInfo
        {
            WorkspacePath = @"C:\temp\TASK-123\run-1",
            RepositoryPath = @"C:\temp\TASK-123\run-1\source"
        };

        var aiCodeAgent = new FakeAiCodeAgent(
            new AiCodeChangeResult
            {
                Summary = "Initial summary",
                ChangedFiles = new[]
                {
                    new AiChangedFile
                    {
                        Path = "src/File.cs",
                        Operation = "modify",
                        Content = "namespace Demo { public class File { public void Execute() { var value = 1; if (value > 0) { value++; } Console.WriteLine(value); } } }"
                    }
                },
                TestNotes = "notes"
            },
            new AiCodeChangeResult
            {
                Summary = "Reformatted summary",
                ChangedFiles = new[]
                {
                    new AiChangedFile
                    {
                        Path = "src/File.cs",
                        Operation = "modify",
                        Content = "namespace Demo;\n\npublic class File\n{\n    public void Execute()\n    {\n    }\n}"
                    }
                },
                TestNotes = "notes"
            });

        var orchestrator = new StubAgentOrchestrator(
            new FakeRepositoryPolicy(RepositoryPolicyResult.Success("example-company")),
            new FakeWorkspaceService(workspace),
            new FakeGitClient(
                GitCloneResult.Success(0, "ok", string.Empty),
                diffSummary: "src/File.cs | 6 ++++++"),
            new FakeRepositoryAnalyzer(new RepositoryAnalysis
            {
                Language = "C#",
                TestCommand = "dotnet test",
                RelevantFiles = new[] { "src/File.cs" }
            }),
            new FakeAgentContextBuilder(new AgentContext
            {
                TaskSummary = "Requirement: Implement email validation",
                RepositoryAnalysisSummary = "Language: C#"
            }),
            aiCodeAgent,
            new SequentialAiChangeValidator(
                AiChangeValidationResult.Failure(AiCodeFormattingHeuristics.BuildCollapsedSourceError("src/File.cs")),
                AiChangeValidationResult.Success(
                    new[]
                    {
                        new AiChangedFile
                        {
                            Path = "src/File.cs",
                            Operation = "modify",
                            Content = "namespace Demo;\n\npublic class File\n{\n    public void Execute()\n    {\n    }\n}"
                        }
                    },
                    Array.Empty<AiChangeWarning>())),
            new FakeFileChangeApplier(new[] { "src/File.cs" }),
            new FakeTestRunner(new[]
            {
                new TestResult
                {
                    Command = "dotnet test",
                    Status = TestExecutionStatus.Passed,
                    ExitCode = 0,
                    AttemptNumber = 1
                }
            }),
            new FakeBranchNameBuilder("ai-agent/TASK-123"),
            new FakePrDescriptionBuilder("PR body"),
            new FakePullRequestService(new PullRequestResult
            {
                PullRequestUrl = "https://github.com/example-company/user-service/pull/44",
                PullRequestNumber = 44,
                Status = PullRequestStatus.Created
            }),
            Options.Create(new AiOptions()),
            NullLogger<StubAgentOrchestrator>.Instance);

        var report = await orchestrator.StartAsync(new CreateTaskExecutionRequest
        {
            TaskId = "TASK-123",
            Title = "Add email validation",
            ParsedTask = new ParsedTask
            {
                RepositoryUrl = "https://github.com/example-company/user-service",
                BaseBranch = "main",
                Requirement = "Implement email validation"
            }
        });

        Assert.Equal(1, aiCodeAgent.FormattingRegenerationCalls);
        Assert.Equal(CloneStatus.Succeeded, report.CloneStatus);
        Assert.Equal(TestExecutionStatus.Passed, report.FinalTestStatus);
        Assert.Contains("AI output was reformatted via regeneration step", report.AiWarnings);
        Assert.Contains(report.Timeline, entry => entry.Step == "AiRegenerateFormatting" && entry.Status == ExecutionTimelineStatus.Succeeded);
    }

    [Fact]
    public async Task StartAsync_WhenFormattingRegenerationValidationFails_ReturnsFailedReport()
    {
        var workspace = new WorkspaceInfo
        {
            WorkspacePath = @"C:\temp\TASK-123\run-1",
            RepositoryPath = @"C:\temp\TASK-123\run-1\source"
        };

        var aiCodeAgent = new FakeAiCodeAgent(
            new AiCodeChangeResult
            {
                Summary = "Initial summary",
                ChangedFiles = new[]
                {
                    new AiChangedFile
                    {
                        Path = "src/File.cs",
                        Operation = "modify",
                        Content = "namespace Demo { public class File { public void Execute() { var value = 1; if (value > 0) { value++; } Console.WriteLine(value); } } }"
                    }
                },
                TestNotes = "notes"
            },
            new AiCodeChangeResult
            {
                Summary = "Still collapsed",
                ChangedFiles = new[]
                {
                    new AiChangedFile
                    {
                        Path = "src/File.cs",
                        Operation = "modify",
                        Content = "namespace Demo { public class File { public void Execute() { var value = 2; if (value > 0) { value++; } Console.WriteLine(value); } } }"
                    }
                },
                TestNotes = "notes"
            });

        var orchestrator = new StubAgentOrchestrator(
            new FakeRepositoryPolicy(RepositoryPolicyResult.Success("example-company")),
            new FakeWorkspaceService(workspace),
            new FakeGitClient(GitCloneResult.Success(0, "ok", string.Empty)),
            new FakeRepositoryAnalyzer(new RepositoryAnalysis
            {
                Language = "C#",
                TestCommand = "dotnet test",
                RelevantFiles = new[] { "src/File.cs" }
            }),
            new FakeAgentContextBuilder(new AgentContext()),
            aiCodeAgent,
            new SequentialAiChangeValidator(
                AiChangeValidationResult.Failure(AiCodeFormattingHeuristics.BuildCollapsedSourceError("src/File.cs")),
                AiChangeValidationResult.Failure(AiCodeFormattingHeuristics.BuildCollapsedSourceError("src/File.cs"))),
            new FakeFileChangeApplier(new[] { "src/File.cs" }),
            new FakeTestRunner(Array.Empty<TestResult>()),
            new FakeBranchNameBuilder("ai-agent/TASK-123"),
            new FakePrDescriptionBuilder("PR body"),
            new FakePullRequestService(new PullRequestResult()),
            Options.Create(new AiOptions()),
            NullLogger<StubAgentOrchestrator>.Instance);

        var report = await orchestrator.StartAsync(new CreateTaskExecutionRequest
        {
            TaskId = "TASK-123",
            ParsedTask = new ParsedTask
            {
                RepositoryUrl = "https://github.com/example-company/user-service",
                BaseBranch = "main",
                Requirement = "Implement email validation"
            }
        });

        Assert.Equal(ExecutionStatus.Failed, report.Status);
        Assert.Equal(1, aiCodeAgent.FormattingRegenerationCalls);
        Assert.Contains("AI formatting regeneration validation failed", report.Message);
        Assert.Contains(report.Timeline, entry => entry.Step == "AiRegenerateFormatting" && entry.Status == ExecutionTimelineStatus.Failed);
    }

    [Fact]
    public async Task StartAsync_WhenFirstTestFails_FixAttemptRunsAndSecondTestPasses()
    {
        var workspace = new WorkspaceInfo
        {
            WorkspacePath = @"C:\temp\TASK-123\run-1",
            RepositoryPath = @"C:\temp\TASK-123\run-1\source"
        };

        var aiCodeAgent = new FakeAiCodeAgent(
            new AiCodeChangeResult
            {
                Summary = "Initial summary",
                ChangedFiles = new[]
                {
                    new AiChangedFile
                    {
                        Path = "src/File.cs",
                        Operation = "modify",
                        Content = "content"
                    }
                },
                TestNotes = "initial"
            },
            new AiCodeChangeResult
            {
                Summary = "Fix summary",
                ChangedFiles = new[]
                {
                    new AiChangedFile
                    {
                        Path = "tests/FileTests.cs",
                        Operation = "modify",
                        Content = "test content"
                    }
                },
                TestNotes = "fix"
            });

        var orchestrator = new StubAgentOrchestrator(
            new FakeRepositoryPolicy(RepositoryPolicyResult.Success("example-company")),
            new FakeWorkspaceService(workspace),
            new FakeGitClient(GitCloneResult.Success(0, "ok", string.Empty)),
            new FakeRepositoryAnalyzer(new RepositoryAnalysis
            {
                Language = "C#",
                TestCommand = "dotnet test",
                RelevantFiles = new[] { "src/File.cs" },
                ExistingTestFiles = new[] { "tests/FileTests.cs" }
            }),
            new FakeAgentContextBuilder(new AgentContext()),
            aiCodeAgent,
            new SequentialAiChangeValidator(
                AiChangeValidationResult.Success(
                    new[]
                    {
                        new AiChangedFile
                        {
                            Path = "src/File.cs",
                            Operation = "modify",
                            Content = "content"
                        }
                    },
                    Array.Empty<AiChangeWarning>()),
                AiChangeValidationResult.Success(
                    new[]
                    {
                        new AiChangedFile
                        {
                            Path = "tests/FileTests.cs",
                            Operation = "modify",
                            Content = "test content"
                        }
                    },
                    Array.Empty<AiChangeWarning>())),
            new SequentialFileChangeApplier(
                new[] { "src/File.cs" },
                new[] { "tests/FileTests.cs" }),
            new FakeTestRunner(new[]
            {
                new TestResult
                {
                    Command = "dotnet test",
                    Status = TestExecutionStatus.Failed,
                    ExitCode = 1,
                    Duration = TimeSpan.FromSeconds(1),
                    Stdout = "stdout",
                    Stderr = "stderr",
                    AttemptNumber = 1
                },
                new TestResult
                {
                    Command = "dotnet test",
                    Status = TestExecutionStatus.Passed,
                    ExitCode = 0,
                    Duration = TimeSpan.FromSeconds(1),
                    AttemptNumber = 2
                }
            }),
            new FakeBranchNameBuilder("ai-agent/TASK-123"),
            new FakePrDescriptionBuilder("PR body"),
            new FakePullRequestService(new PullRequestResult
            {
                PullRequestUrl = "https://github.com/example-company/user-service/pull/43",
                PullRequestNumber = 43,
                Status = PullRequestStatus.Created
            }),
            Options.Create(new AiOptions { MaxTestFixAttempts = 1 }),
            NullLogger<StubAgentOrchestrator>.Instance);

        var report = await orchestrator.StartAsync(new CreateTaskExecutionRequest
        {
            TaskId = "TASK-123",
            ParsedTask = new ParsedTask
            {
                RepositoryUrl = "https://github.com/example-company/user-service",
                BaseBranch = "main",
                Requirement = "Implement email validation"
            }
        });

        Assert.True(report.AiFixAttempted);
        Assert.Equal("Fix summary", report.AiFixSummary);
        Assert.Equal(TestExecutionStatus.Passed, report.FinalTestStatus);
        Assert.Contains("src/File.cs", report.ChangedFiles);
        Assert.Contains("tests/FileTests.cs", report.ChangedFiles);
        Assert.Contains("tests/FileTests.cs", report.ChangedFilesAfterFix);
        Assert.Equal(GitFinalizationStatus.Completed, report.GitFinalizationStatus);
        Assert.Contains(report.Timeline, entry => entry.Step == "TestRunAttempt1" && entry.Status == ExecutionTimelineStatus.Failed);
        Assert.Contains(report.Timeline, entry => entry.Step == "AiFixAttempt" && entry.Status == ExecutionTimelineStatus.Succeeded);
        Assert.Contains(report.Timeline, entry => entry.Step == "ApplyFixChanges" && entry.Status == ExecutionTimelineStatus.Succeeded);
        Assert.Contains(report.Timeline, entry => entry.Step == "TestRunAttempt2" && entry.Status == ExecutionTimelineStatus.Succeeded);
    }

    [Fact]
    public async Task StartAsync_WhenSecondTestFails_FinalStatusIsFailed()
    {
        var workspace = new WorkspaceInfo
        {
            WorkspacePath = @"C:\temp\TASK-123\run-1",
            RepositoryPath = @"C:\temp\TASK-123\run-1\source"
        };

        var orchestrator = new StubAgentOrchestrator(
            new FakeRepositoryPolicy(RepositoryPolicyResult.Success("example-company")),
            new FakeWorkspaceService(workspace),
            new FakeGitClient(GitCloneResult.Success(0, "ok", string.Empty)),
            new FakeRepositoryAnalyzer(new RepositoryAnalysis
            {
                Language = "C#",
                TestCommand = "dotnet test",
                RelevantFiles = new[] { "src/File.cs" },
                ExistingTestFiles = new[] { "tests/FileTests.cs" }
            }),
            new FakeAgentContextBuilder(new AgentContext()),
            new FakeAiCodeAgent(
                new AiCodeChangeResult
                {
                    Summary = "Initial summary",
                    ChangedFiles = new[]
                    {
                        new AiChangedFile
                        {
                            Path = "src/File.cs",
                            Operation = "modify",
                            Content = "content"
                        }
                    },
                    TestNotes = "initial"
                },
                new AiCodeChangeResult
                {
                    Summary = "Fix summary",
                    ChangedFiles = new[]
                    {
                        new AiChangedFile
                        {
                            Path = "tests/FileTests.cs",
                            Operation = "modify",
                            Content = "test content"
                        }
                    },
                    TestNotes = "fix"
                }),
            new SequentialAiChangeValidator(
                AiChangeValidationResult.Success(
                    new[]
                    {
                        new AiChangedFile
                        {
                            Path = "src/File.cs",
                            Operation = "modify",
                            Content = "content"
                        }
                    },
                    Array.Empty<AiChangeWarning>()),
                AiChangeValidationResult.Success(
                    new[]
                    {
                        new AiChangedFile
                        {
                            Path = "tests/FileTests.cs",
                            Operation = "modify",
                            Content = "test content"
                        }
                    },
                    Array.Empty<AiChangeWarning>())),
            new SequentialFileChangeApplier(
                new[] { "src/File.cs" },
                new[] { "tests/FileTests.cs" }),
            new FakeTestRunner(new[]
            {
                new TestResult
                {
                    Command = "dotnet test",
                    Status = TestExecutionStatus.Failed,
                    ExitCode = 1,
                    Duration = TimeSpan.FromSeconds(1),
                    AttemptNumber = 1
                },
                new TestResult
                {
                    Command = "dotnet test",
                    Status = TestExecutionStatus.Failed,
                    ExitCode = 1,
                    Duration = TimeSpan.FromSeconds(1),
                    AttemptNumber = 2
                }
            }),
            new FakeBranchNameBuilder("ai-agent/TASK-123"),
            new FakePrDescriptionBuilder("PR body"),
            new FakePullRequestService(new PullRequestResult()),
            Options.Create(new AiOptions { MaxTestFixAttempts = 1 }),
            NullLogger<StubAgentOrchestrator>.Instance);

        var report = await orchestrator.StartAsync(new CreateTaskExecutionRequest
        {
            TaskId = "TASK-123",
            ParsedTask = new ParsedTask
            {
                RepositoryUrl = "https://github.com/example-company/user-service",
                BaseBranch = "main",
                Requirement = "Implement email validation"
            }
        });

        Assert.Equal(ExecutionStatus.Failed, report.Status);
        Assert.Equal(TestExecutionStatus.Failed, report.FinalTestStatus);
        Assert.Contains("Tests failed after AI fix attempt", report.FailureReason);
    }

    [Fact]
    public async Task StartAsync_WhenTestCommandUnsupported_NextStepIsBlocked()
    {
        var workspace = new WorkspaceInfo
        {
            WorkspacePath = @"C:\temp\TASK-123\run-1",
            RepositoryPath = @"C:\temp\TASK-123\run-1\source"
        };

        var orchestrator = new StubAgentOrchestrator(
            new FakeRepositoryPolicy(RepositoryPolicyResult.Success("example-company")),
            new FakeWorkspaceService(workspace),
            new FakeGitClient(GitCloneResult.Success(0, "ok", string.Empty)),
            new FakeRepositoryAnalyzer(new RepositoryAnalysis
            {
                Language = "C#",
                TestCommand = "custom test"
            }),
            new FakeAgentContextBuilder(new AgentContext()),
            new FakeAiCodeAgent(new AiCodeChangeResult
            {
                Summary = "Initial summary",
                ChangedFiles = new[]
                {
                    new AiChangedFile
                    {
                        Path = "src/File.cs",
                        Operation = "modify",
                        Content = "content"
                    }
                },
                TestNotes = "initial"
            }),
            new SequentialAiChangeValidator(
                AiChangeValidationResult.Success(
                    new[]
                    {
                        new AiChangedFile
                        {
                            Path = "src/File.cs",
                            Operation = "modify",
                            Content = "content"
                        }
                    },
                    Array.Empty<AiChangeWarning>())),
            new SequentialFileChangeApplier(new[] { "src/File.cs" }),
            new FakeTestRunner(new[]
            {
                new TestResult
                {
                    Command = "custom test",
                    Status = TestExecutionStatus.Unsupported,
                    ExitCode = -1,
                    Duration = TimeSpan.Zero,
                    AttemptNumber = 1,
                    Stderr = "unsupported"
                }
            }),
            new FakeBranchNameBuilder("ai-agent/TASK-123"),
            new FakePrDescriptionBuilder("PR body"),
            new FakePullRequestService(new PullRequestResult()),
            Options.Create(new AiOptions { MaxTestFixAttempts = 1 }),
            NullLogger<StubAgentOrchestrator>.Instance);

        var report = await orchestrator.StartAsync(new CreateTaskExecutionRequest
        {
            TaskId = "TASK-123",
            ParsedTask = new ParsedTask
            {
                RepositoryUrl = "https://github.com/example-company/user-service",
                BaseBranch = "main",
                Requirement = "Implement email validation"
            }
        });

        Assert.Equal(ExecutionStatus.Failed, report.Status);
        Assert.Equal(TestExecutionStatus.Unsupported, report.FinalTestStatus);
        Assert.Contains("unsupported", report.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(GitFinalizationStatus.Skipped, report.GitFinalizationStatus);
    }

    [Fact]
    public async Task StartAsync_WhenNoChangesAfterTests_CommitPushAndPrAreSkipped()
    {
        var workspace = new WorkspaceInfo
        {
            WorkspacePath = @"C:\temp\TASK-123\run-1",
            RepositoryPath = @"C:\temp\TASK-123\run-1\source"
        };

        var pullRequestService = new FakePullRequestService(new PullRequestResult
        {
            PullRequestUrl = "https://github.com/example-company/user-service/pull/44",
            PullRequestNumber = 44,
            Status = PullRequestStatus.Created
        });

        var orchestrator = new StubAgentOrchestrator(
            new FakeRepositoryPolicy(RepositoryPolicyResult.Success("example-company")),
            new FakeWorkspaceService(workspace),
            new FakeGitClient(
                GitCloneResult.Success(0, "ok", string.Empty),
                hasChanges: false),
            new FakeRepositoryAnalyzer(new RepositoryAnalysis
            {
                Language = "C#",
                TestCommand = "dotnet test"
            }),
            new FakeAgentContextBuilder(new AgentContext()),
            new FakeAiCodeAgent(new AiCodeChangeResult
            {
                Summary = "Initial summary",
                ChangedFiles = new[]
                {
                    new AiChangedFile
                    {
                        Path = "src/File.cs",
                        Operation = "modify",
                        Content = "content"
                    }
                }
            }),
            new FakeAiChangeValidator(AiChangeValidationResult.Success(
                new[]
                {
                    new AiChangedFile
                    {
                        Path = "src/File.cs",
                        Operation = "modify",
                        Content = "content"
                    }
                },
                Array.Empty<AiChangeWarning>())),
            new FakeFileChangeApplier(new[] { "src/File.cs" }),
            new FakeTestRunner(new[]
            {
                new TestResult
                {
                    Command = "dotnet test",
                    Status = TestExecutionStatus.Passed,
                    ExitCode = 0,
                    AttemptNumber = 1
                }
            }),
            new FakeBranchNameBuilder("ai-agent/TASK-123"),
            new FakePrDescriptionBuilder("PR body"),
            pullRequestService,
            Options.Create(new AiOptions()),
            NullLogger<StubAgentOrchestrator>.Instance);

        var report = await orchestrator.StartAsync(new CreateTaskExecutionRequest
        {
            TaskId = "TASK-123",
            Title = "Add email validation",
            ParsedTask = new ParsedTask
            {
                RepositoryUrl = "https://github.com/example-company/user-service",
                BaseBranch = "main",
                Requirement = "Implement email validation"
            }
        });

        Assert.Equal(ExecutionStatus.Failed, report.Status);
        Assert.Equal(GitFinalizationStatus.Skipped, report.GitFinalizationStatus);
        Assert.Contains("No repository changes detected", report.FailureReason);
        Assert.False(pullRequestService.WasCalled);
    }

    [Fact]
    public async Task StartAsync_WhenTestsFail_GitHubPullRequestIsNotCalled()
    {
        var workspace = new WorkspaceInfo
        {
            WorkspacePath = @"C:\temp\TASK-123\run-1",
            RepositoryPath = @"C:\temp\TASK-123\run-1\source"
        };

        var pullRequestService = new FakePullRequestService(new PullRequestResult());

        var orchestrator = new StubAgentOrchestrator(
            new FakeRepositoryPolicy(RepositoryPolicyResult.Success("example-company")),
            new FakeWorkspaceService(workspace),
            new FakeGitClient(GitCloneResult.Success(0, "ok", string.Empty)),
            new FakeRepositoryAnalyzer(new RepositoryAnalysis
            {
                Language = "C#",
                TestCommand = "dotnet test"
            }),
            new FakeAgentContextBuilder(new AgentContext()),
            new FakeAiCodeAgent(new AiCodeChangeResult
            {
                Summary = "Initial summary",
                ChangedFiles = new[]
                {
                    new AiChangedFile
                    {
                        Path = "src/File.cs",
                        Operation = "modify",
                        Content = "content"
                    }
                }
            }),
            new FakeAiChangeValidator(AiChangeValidationResult.Success(
                new[]
                {
                    new AiChangedFile
                    {
                        Path = "src/File.cs",
                        Operation = "modify",
                        Content = "content"
                    }
                },
                Array.Empty<AiChangeWarning>())),
            new FakeFileChangeApplier(new[] { "src/File.cs" }),
            new FakeTestRunner(new[]
            {
                new TestResult
                {
                    Command = "dotnet test",
                    Status = TestExecutionStatus.Failed,
                    ExitCode = 1,
                    AttemptNumber = 1
                },
                new TestResult
                {
                    Command = "dotnet test",
                    Status = TestExecutionStatus.Failed,
                    ExitCode = 1,
                    AttemptNumber = 2
                }
            }),
            new FakeBranchNameBuilder("ai-agent/TASK-123"),
            new FakePrDescriptionBuilder("PR body"),
            pullRequestService,
            Options.Create(new AiOptions { MaxTestFixAttempts = 1 }),
            NullLogger<StubAgentOrchestrator>.Instance);

        var report = await orchestrator.StartAsync(new CreateTaskExecutionRequest
        {
            TaskId = "TASK-123",
            Title = "Add email validation",
            ParsedTask = new ParsedTask
            {
                RepositoryUrl = "https://github.com/example-company/user-service",
                BaseBranch = "main",
                Requirement = "Implement email validation"
            }
        });

        Assert.Equal(ExecutionStatus.Failed, report.Status);
        Assert.False(pullRequestService.WasCalled);
    }

    [Fact]
    public async Task StartAsync_WhenDuplicatePullRequestExists_ReturnsExistingUrl()
    {
        var workspace = new WorkspaceInfo
        {
            WorkspacePath = @"C:\temp\TASK-123\run-1",
            RepositoryPath = @"C:\temp\TASK-123\run-1\source"
        };

        var pullRequestService = new FakePullRequestService(new PullRequestResult
        {
            PullRequestUrl = "https://github.com/example-company/user-service/pull/99",
            PullRequestNumber = 99,
            Status = PullRequestStatus.AlreadyExists
        });

        var orchestrator = new StubAgentOrchestrator(
            new FakeRepositoryPolicy(RepositoryPolicyResult.Success("example-company")),
            new FakeWorkspaceService(workspace),
            new FakeGitClient(GitCloneResult.Success(0, "ok", string.Empty)),
            new FakeRepositoryAnalyzer(new RepositoryAnalysis
            {
                Language = "C#",
                TestCommand = "dotnet test"
            }),
            new FakeAgentContextBuilder(new AgentContext()),
            new FakeAiCodeAgent(new AiCodeChangeResult
            {
                Summary = "Initial summary",
                ChangedFiles = new[]
                {
                    new AiChangedFile
                    {
                        Path = "src/File.cs",
                        Operation = "modify",
                        Content = "content"
                    }
                },
                Usage = new AiUsageInfo
                {
                    Model = "gpt-4.1-mini"
                }
            }),
            new FakeAiChangeValidator(AiChangeValidationResult.Success(
                new[]
                {
                    new AiChangedFile
                    {
                        Path = "src/File.cs",
                        Operation = "modify",
                        Content = "content"
                    }
                },
                Array.Empty<AiChangeWarning>())),
            new FakeFileChangeApplier(new[] { "src/File.cs" }),
            new FakeTestRunner(new[]
            {
                new TestResult
                {
                    Command = "dotnet test",
                    Status = TestExecutionStatus.Passed,
                    ExitCode = 0,
                    AttemptNumber = 1
                }
            }),
            new FakeBranchNameBuilder("ai-agent/TASK-123"),
            new FakePrDescriptionBuilder("PR body"),
            pullRequestService,
            Options.Create(new AiOptions()),
            NullLogger<StubAgentOrchestrator>.Instance);

        var report = await orchestrator.StartAsync(new CreateTaskExecutionRequest
        {
            TaskId = "TASK-123",
            Title = "Add email validation",
            ParsedTask = new ParsedTask
            {
                RepositoryUrl = "https://github.com/example-company/user-service",
                BaseBranch = "main",
                Requirement = "Implement email validation"
            }
        });

        Assert.Equal(PullRequestStatus.AlreadyExists, report.PullRequestStatus);
        Assert.Equal("https://github.com/example-company/user-service/pull/99", report.PullRequestUrl);
        Assert.Equal(GitFinalizationStatus.AlreadyExists, report.GitFinalizationStatus);
    }

    [Fact]
    public async Task StartAsync_WhenInitialApplyAddsNewProject_SyncsSolutionBeforeTests()
    {
        var workspace = new WorkspaceInfo
        {
            WorkspacePath = @"C:\temp\TASK-123\run-1",
            RepositoryPath = @"C:\temp\TASK-123\run-1\source"
        };
        var solutionSynchronizer = new FakeSolutionProjectSynchronizer(
            DotNetSolutionBaseline.Success(
                @"C:\temp\TASK-123\run-1\source\App.sln",
                new[] { "src/App/App.csproj" },
                new[] { "src/App/App.csproj" }),
            DotNetSolutionSyncResult.Success(
                @"C:\temp\TASK-123\run-1\source\App.sln",
                new[] { "tests/App.Tests/App.Tests.csproj" },
                Array.Empty<string>(),
                "Added test project"));
        var testRunner = new FakeTestRunner(new[]
        {
            new TestResult
            {
                Command = "dotnet test",
                Status = TestExecutionStatus.Passed,
                ExitCode = 0,
                AttemptNumber = 1
            }
        });

        var orchestrator = new StubAgentOrchestrator(
            new FakeRepositoryPolicy(RepositoryPolicyResult.Success("example-company")),
            new FakeWorkspaceService(workspace),
            new FakeGitClient(GitCloneResult.Success(0, "ok", string.Empty)),
            new FakeRepositoryAnalyzer(new RepositoryAnalysis
            {
                Language = "C#",
                BuildTool = "dotnet",
                TestCommand = "dotnet test",
                ProjectFiles = new[] { "App.sln", "src/App/App.csproj" }
            }),
            new FakeAgentContextBuilder(new AgentContext()),
            new FakeAiCodeAgent(new AiCodeChangeResult
            {
                Summary = "Initial summary",
                ChangedFiles = new[]
                {
                    new AiChangedFile
                    {
                        Path = "tests/App.Tests/App.Tests.csproj",
                        Operation = "create",
                        Content = "<Project />"
                    }
                }
            }),
            new FakeAiChangeValidator(AiChangeValidationResult.Success(
                new[]
                {
                    new AiChangedFile
                    {
                        Path = "tests/App.Tests/App.Tests.csproj",
                        Operation = "create",
                        Content = "<Project />"
                    }
                },
                Array.Empty<AiChangeWarning>())),
            new FakeFileChangeApplier(new[] { "tests/App.Tests/App.Tests.csproj" }),
            testRunner,
            new FakeBranchNameBuilder("ai-agent/TASK-123"),
            new FakePrDescriptionBuilder("PR body"),
            new FakePullRequestService(new PullRequestResult()),
            Options.Create(new AiOptions()),
            NullLogger<StubAgentOrchestrator>.Instance,
            solutionProjectSynchronizer: solutionSynchronizer);

        var report = await orchestrator.StartAsync(new CreateTaskExecutionRequest
        {
            TaskId = "TASK-123",
            Title = "Add email validation",
            ParsedTask = new ParsedTask
            {
                RepositoryUrl = "https://github.com/example-company/user-service",
                BaseBranch = "main",
                Requirement = "Implement email validation"
            }
        });

        Assert.Equal(1, solutionSynchronizer.CaptureCalls);
        Assert.Equal(1, solutionSynchronizer.SyncCalls);
        Assert.Equal(1, testRunner.RunCalls);
        Assert.Equal(@"C:\temp\TASK-123\run-1\source\App.sln", report.SolutionFile);
        Assert.Equal(new[] { "tests/App.Tests/App.Tests.csproj" }, report.AddedProjectsToSolution);
        Assert.Contains(report.Timeline, entry => entry.Step == "DotNetSolutionBaselineCapture" && entry.Status == ExecutionTimelineStatus.Succeeded);
        Assert.Contains(report.Timeline, entry => entry.Step == "DotNetSolutionSync" && entry.Status == ExecutionTimelineStatus.Succeeded);
    }

    [Fact]
    public async Task StartAsync_WhenSolutionSyncFails_ReturnsFailedReportWithoutRunningTests()
    {
        var workspace = new WorkspaceInfo
        {
            WorkspacePath = @"C:\temp\TASK-123\run-1",
            RepositoryPath = @"C:\temp\TASK-123\run-1\source"
        };
        var solutionSynchronizer = new FakeSolutionProjectSynchronizer(
            DotNetSolutionBaseline.Success(
                @"C:\temp\TASK-123\run-1\source\App.sln",
                new[] { "src/App/App.csproj" },
                new[] { "src/App/App.csproj" }),
            DotNetSolutionSyncResult.Failure(
                @"C:\temp\TASK-123\run-1\source\App.sln",
                "Failed to add project to the solution."));
        var testRunner = new FakeTestRunner(Array.Empty<TestResult>());

        var orchestrator = new StubAgentOrchestrator(
            new FakeRepositoryPolicy(RepositoryPolicyResult.Success("example-company")),
            new FakeWorkspaceService(workspace),
            new FakeGitClient(GitCloneResult.Success(0, "ok", string.Empty)),
            new FakeRepositoryAnalyzer(new RepositoryAnalysis
            {
                Language = "C#",
                BuildTool = "dotnet",
                TestCommand = "dotnet test",
                ProjectFiles = new[] { "App.sln" }
            }),
            new FakeAgentContextBuilder(new AgentContext()),
            new FakeAiCodeAgent(new AiCodeChangeResult
            {
                Summary = "Initial summary",
                ChangedFiles = new[]
                {
                    new AiChangedFile
                    {
                        Path = "tests/App.Tests/App.Tests.csproj",
                        Operation = "create",
                        Content = "<Project />"
                    }
                }
            }),
            new FakeAiChangeValidator(AiChangeValidationResult.Success(
                new[]
                {
                    new AiChangedFile
                    {
                        Path = "tests/App.Tests/App.Tests.csproj",
                        Operation = "create",
                        Content = "<Project />"
                    }
                },
                Array.Empty<AiChangeWarning>())),
            new FakeFileChangeApplier(new[] { "tests/App.Tests/App.Tests.csproj" }),
            testRunner,
            new FakeBranchNameBuilder("ai-agent/TASK-123"),
            new FakePrDescriptionBuilder("PR body"),
            new FakePullRequestService(new PullRequestResult()),
            Options.Create(new AiOptions()),
            NullLogger<StubAgentOrchestrator>.Instance,
            solutionProjectSynchronizer: solutionSynchronizer);

        var report = await orchestrator.StartAsync(new CreateTaskExecutionRequest
        {
            TaskId = "TASK-123",
            Title = "Add email validation",
            ParsedTask = new ParsedTask
            {
                RepositoryUrl = "https://github.com/example-company/user-service",
                BaseBranch = "main",
                Requirement = "Implement email validation"
            }
        });

        Assert.Equal(ExecutionStatus.Failed, report.Status);
        Assert.Equal(1, solutionSynchronizer.CaptureCalls);
        Assert.Equal(1, solutionSynchronizer.SyncCalls);
        Assert.Equal(0, testRunner.RunCalls);
        Assert.Contains("Failed to add project", report.Message);
        Assert.Contains(report.Timeline, entry => entry.Step == "DotNetSolutionSync" && entry.Status == ExecutionTimelineStatus.Failed);
    }

    [Fact]
    public async Task StartAsync_WhenFixAddsNewProject_SyncsSolutionAgainBeforeSecondTestRun()
    {
        var workspace = new WorkspaceInfo
        {
            WorkspacePath = @"C:\temp\TASK-123\run-1",
            RepositoryPath = @"C:\temp\TASK-123\run-1\source"
        };
        var solutionSynchronizer = new FakeSolutionProjectSynchronizer(
            DotNetSolutionBaseline.Success(
                @"C:\temp\TASK-123\run-1\source\App.sln",
                new[] { "src/App/App.csproj" },
                new[] { "src/App/App.csproj" }),
            DotNetSolutionSyncResult.Success(
                @"C:\temp\TASK-123\run-1\source\App.sln",
                Array.Empty<string>(),
                Array.Empty<string>(),
                "Initial sync complete"),
            DotNetSolutionSyncResult.Success(
                @"C:\temp\TASK-123\run-1\source\App.sln",
                new[] { "tests/App.Tests/App.Tests.csproj" },
                Array.Empty<string>(),
                "Fix sync complete"));
        var testRunner = new FakeTestRunner(new[]
        {
            new TestResult
            {
                Command = "dotnet test",
                Status = TestExecutionStatus.Failed,
                ExitCode = 1,
                AttemptNumber = 1
            },
            new TestResult
            {
                Command = "dotnet test",
                Status = TestExecutionStatus.Passed,
                ExitCode = 0,
                AttemptNumber = 2
            }
        });

        var orchestrator = new StubAgentOrchestrator(
            new FakeRepositoryPolicy(RepositoryPolicyResult.Success("example-company")),
            new FakeWorkspaceService(workspace),
            new FakeGitClient(GitCloneResult.Success(0, "ok", string.Empty)),
            new FakeRepositoryAnalyzer(new RepositoryAnalysis
            {
                Language = "C#",
                BuildTool = "dotnet",
                TestCommand = "dotnet test",
                ProjectFiles = new[] { "App.sln" }
            }),
            new FakeAgentContextBuilder(new AgentContext()),
            new FakeAiCodeAgent(
                new AiCodeChangeResult
                {
                    Summary = "Initial summary",
                    ChangedFiles = new[]
                    {
                        new AiChangedFile
                        {
                            Path = "src/File.cs",
                            Operation = "modify",
                            Content = "content"
                        }
                    }
                },
                new AiCodeChangeResult
                {
                    Summary = "Fix summary",
                    ChangedFiles = new[]
                    {
                        new AiChangedFile
                        {
                            Path = "tests/App.Tests/App.Tests.csproj",
                            Operation = "create",
                            Content = "<Project />"
                        }
                    }
                }),
            new SequentialAiChangeValidator(
                AiChangeValidationResult.Success(
                    new[]
                    {
                        new AiChangedFile
                        {
                            Path = "src/File.cs",
                            Operation = "modify",
                            Content = "content"
                        }
                    },
                    Array.Empty<AiChangeWarning>()),
                AiChangeValidationResult.Success(
                    new[]
                    {
                        new AiChangedFile
                        {
                            Path = "tests/App.Tests/App.Tests.csproj",
                            Operation = "create",
                            Content = "<Project />"
                        }
                    },
                    Array.Empty<AiChangeWarning>())),
            new SequentialFileChangeApplier(
                new[] { "src/File.cs" },
                new[] { "tests/App.Tests/App.Tests.csproj" }),
            testRunner,
            new FakeBranchNameBuilder("ai-agent/TASK-123"),
            new FakePrDescriptionBuilder("PR body"),
            new FakePullRequestService(new PullRequestResult()),
            Options.Create(new AiOptions { MaxTestFixAttempts = 1 }),
            NullLogger<StubAgentOrchestrator>.Instance,
            solutionProjectSynchronizer: solutionSynchronizer);

        var report = await orchestrator.StartAsync(new CreateTaskExecutionRequest
        {
            TaskId = "TASK-123",
            Title = "Add email validation",
            ParsedTask = new ParsedTask
            {
                RepositoryUrl = "https://github.com/example-company/user-service",
                BaseBranch = "main",
                Requirement = "Implement email validation"
            }
        });

        Assert.Equal(1, solutionSynchronizer.CaptureCalls);
        Assert.Equal(2, solutionSynchronizer.SyncCalls);
        Assert.Equal(2, testRunner.RunCalls);
        Assert.Equal(TestExecutionStatus.Passed, report.FinalTestStatus);
        Assert.Equal(new[] { "tests/App.Tests/App.Tests.csproj" }, report.AddedProjectsToSolution);
        Assert.Contains(report.Timeline, entry => entry.Step == "DotNetSolutionSyncAfterFix" && entry.Status == ExecutionTimelineStatus.Succeeded);
    }

    [Fact]
    public async Task StartAsync_WhenProjectChangesExistButNoSolutionUpdateWasNeeded_AddsDiagnosticTimelineMessage()
    {
        var workspace = new WorkspaceInfo
        {
            WorkspacePath = @"C:\temp\TASK-123\run-1",
            RepositoryPath = @"C:\temp\TASK-123\run-1\source"
        };
        var solutionSynchronizer = new FakeSolutionProjectSynchronizer(
            DotNetSolutionBaseline.Success(
                @"C:\temp\TASK-123\run-1\source\App.sln",
                new[] { "src/App/App.csproj", "tests/App.Tests/App.Tests.csproj" },
                new[] { "src/App/App.csproj", "tests/App.Tests/App.Tests.csproj" }),
            DotNetSolutionSyncResult.Success(
                @"C:\temp\TASK-123\run-1\source\App.sln",
                Array.Empty<string>(),
                Array.Empty<string>(),
                "Solution sync completed. Added: 0, Removed: 0."));
        var testRunner = new FakeTestRunner(new[]
        {
            new TestResult
            {
                Command = "dotnet test",
                Status = TestExecutionStatus.Passed,
                ExitCode = 0,
                AttemptNumber = 1
            }
        });

        var orchestrator = new StubAgentOrchestrator(
            new FakeRepositoryPolicy(RepositoryPolicyResult.Success("example-company")),
            new FakeWorkspaceService(workspace),
            new FakeGitClient(GitCloneResult.Success(0, "ok", string.Empty)),
            new FakeRepositoryAnalyzer(new RepositoryAnalysis
            {
                Language = "C#",
                BuildTool = "dotnet",
                TestCommand = "dotnet test",
                ProjectFiles = new[] { "App.sln", "src/App/App.csproj", "tests/App.Tests/App.Tests.csproj" }
            }),
            new FakeAgentContextBuilder(new AgentContext()),
            new FakeAiCodeAgent(new AiCodeChangeResult
            {
                Summary = "Initial summary",
                ChangedFiles = new[]
                {
                    new AiChangedFile
                    {
                        Path = "tests/App.Tests/App.Tests.csproj",
                        Operation = "modify",
                        Content = "<Project />"
                    }
                }
            }),
            new FakeAiChangeValidator(AiChangeValidationResult.Success(
                new[]
                {
                    new AiChangedFile
                    {
                        Path = "tests/App.Tests/App.Tests.csproj",
                        Operation = "modify",
                        Content = "<Project />"
                    }
                },
                Array.Empty<AiChangeWarning>())),
            new FakeFileChangeApplier(new[] { "tests/App.Tests/App.Tests.csproj" }),
            testRunner,
            new FakeBranchNameBuilder("ai-agent/TASK-123"),
            new FakePrDescriptionBuilder("PR body"),
            new FakePullRequestService(new PullRequestResult()),
            Options.Create(new AiOptions()),
            NullLogger<StubAgentOrchestrator>.Instance,
            solutionProjectSynchronizer: solutionSynchronizer);

        var report = await orchestrator.StartAsync(new CreateTaskExecutionRequest
        {
            TaskId = "TASK-123",
            Title = "Add email validation",
            ParsedTask = new ParsedTask
            {
                RepositoryUrl = "https://github.com/example-company/user-service",
                BaseBranch = "main",
                Requirement = "Implement email validation"
            }
        });

        Assert.Equal(1, solutionSynchronizer.SyncCalls);
        Assert.Equal(TestExecutionStatus.Passed, report.FinalTestStatus);
        Assert.Contains(
            report.Timeline,
            entry => entry.Step == "DotNetSolutionSync" &&
                     entry.Status == ExecutionTimelineStatus.Succeeded &&
                     entry.Message.Contains(".csproj changes were detected, but no solution membership changes were needed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_WhenProjectChangesExistWithoutSolutionBaseline_AddsDiagnosticSkipMessage()
    {
        var workspace = new WorkspaceInfo
        {
            WorkspacePath = @"C:\temp\TASK-123\run-1",
            RepositoryPath = @"C:\temp\TASK-123\run-1\source"
        };
        var solutionSynchronizer = new FakeSolutionProjectSynchronizer(
            DotNetSolutionBaseline.Unsupported("Solution sync skipped because no solution file was detected."));
        var testRunner = new FakeTestRunner(new[]
        {
            new TestResult
            {
                Command = "dotnet test",
                Status = TestExecutionStatus.Passed,
                ExitCode = 0,
                AttemptNumber = 1
            }
        });

        var orchestrator = new StubAgentOrchestrator(
            new FakeRepositoryPolicy(RepositoryPolicyResult.Success("example-company")),
            new FakeWorkspaceService(workspace),
            new FakeGitClient(GitCloneResult.Success(0, "ok", string.Empty)),
            new FakeRepositoryAnalyzer(new RepositoryAnalysis
            {
                Language = "C#",
                BuildTool = "dotnet",
                TestCommand = "dotnet test",
                ProjectFiles = new[] { "src/App/App.csproj" }
            }),
            new FakeAgentContextBuilder(new AgentContext()),
            new FakeAiCodeAgent(new AiCodeChangeResult
            {
                Summary = "Initial summary",
                ChangedFiles = new[]
                {
                    new AiChangedFile
                    {
                        Path = "tests/App.Tests/App.Tests.csproj",
                        Operation = "create",
                        Content = "<Project />"
                    }
                }
            }),
            new FakeAiChangeValidator(AiChangeValidationResult.Success(
                new[]
                {
                    new AiChangedFile
                    {
                        Path = "tests/App.Tests/App.Tests.csproj",
                        Operation = "create",
                        Content = "<Project />"
                    }
                },
                Array.Empty<AiChangeWarning>())),
            new FakeFileChangeApplier(new[] { "tests/App.Tests/App.Tests.csproj" }),
            testRunner,
            new FakeBranchNameBuilder("ai-agent/TASK-123"),
            new FakePrDescriptionBuilder("PR body"),
            new FakePullRequestService(new PullRequestResult()),
            Options.Create(new AiOptions()),
            NullLogger<StubAgentOrchestrator>.Instance,
            solutionProjectSynchronizer: solutionSynchronizer);

        var report = await orchestrator.StartAsync(new CreateTaskExecutionRequest
        {
            TaskId = "TASK-123",
            Title = "Add email validation",
            ParsedTask = new ParsedTask
            {
                RepositoryUrl = "https://github.com/example-company/user-service",
                BaseBranch = "main",
                Requirement = "Implement email validation"
            }
        });

        Assert.Equal(0, solutionSynchronizer.SyncCalls);
        Assert.Equal(TestExecutionStatus.Passed, report.FinalTestStatus);
        Assert.Contains(
            report.Timeline,
            entry => entry.Step == "DotNetSolutionSync" &&
                     entry.Status == ExecutionTimelineStatus.Skipped &&
                     entry.Message.Contains(".csproj changes were applied without automatic solution membership updates", StringComparison.Ordinal));
    }

    private sealed class FakeRepositoryPolicy : IRepositoryPolicy
    {
        private readonly RepositoryPolicyResult _result;

        public FakeRepositoryPolicy(RepositoryPolicyResult result)
        {
            _result = result;
        }

        public RepositoryPolicyResult Validate(string repositoryUrl) => _result;
    }

    private sealed class FakeWorkspaceService : IWorkspaceService
    {
        private readonly WorkspaceInfo _workspaceInfo;

        public FakeWorkspaceService(WorkspaceInfo workspaceInfo)
        {
            _workspaceInfo = workspaceInfo;
        }

        public Task<WorkspaceInfo> CreateAsync(string taskId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_workspaceInfo);
        }
    }

    private sealed class FakeGitClient : IGitClient
    {
        private readonly GitCloneResult _result;
        private readonly bool _hasChanges;
        private readonly IReadOnlyList<string> _changedFiles;
        private readonly IReadOnlyList<string> _committedFiles;
        private readonly GitCommandResult _createBranchResult;
        private readonly GitCommandResult _commitResult;
        private readonly GitCommandResult _pushResult;
        private readonly string _diffSummary;

        public FakeGitClient(
            GitCloneResult result,
            bool hasChanges = true,
            IReadOnlyList<string>? changedFiles = null,
            IReadOnlyList<string>? committedFiles = null,
            GitCommandResult? createBranchResult = null,
            GitCommandResult? commitResult = null,
            GitCommandResult? pushResult = null,
            string diffSummary = "src/File.cs | 4 ++--")
        {
            _result = result;
            _hasChanges = hasChanges;
            _changedFiles = changedFiles ?? new[] { "src/File.cs" };
            _committedFiles = committedFiles ?? _changedFiles;
            _createBranchResult = createBranchResult ?? GitCommandResult.Success("ok", 0, string.Empty, string.Empty);
            _commitResult = commitResult ?? GitCommandResult.Success("ok", 0, string.Empty, string.Empty);
            _pushResult = pushResult ?? GitCommandResult.Success("ok", 0, string.Empty, string.Empty);
            _diffSummary = diffSummary;
        }

        public Task<GitCloneResult> CloneAsync(
            string repositoryUrl,
            string baseBranch,
            string targetPath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }

        public Task<GitCommandResult> CreateBranchAsync(
            string repoPath,
            string branchName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_createBranchResult);
        }

        public Task<bool> HasChangesAsync(
            string repoPath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_hasChanges);
        }

        public Task<IReadOnlyList<string>> GetChangedFilesAsync(
            string repoPath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_changedFiles);
        }

        public Task<IReadOnlyList<string>> GetCommittedFilesAsync(
            string repoPath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_committedFiles);
        }

        public Task<string> GetDiffSummaryAsync(
            string repoPath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_diffSummary);
        }

        public Task<GitCommandResult> CommitAsync(
            string repoPath,
            string message,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_commitResult);
        }

        public Task<GitCommandResult> PushAsync(
            string repoPath,
            string branchName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_pushResult);
        }
    }

    private sealed class FakeRepositoryAnalyzer : IRepositoryAnalyzer
    {
        private readonly RepositoryAnalysis _analysis;

        public FakeRepositoryAnalyzer(RepositoryAnalysis analysis)
        {
            _analysis = analysis;
        }

        public Task<RepositoryAnalysis> AnalyzeAsync(
            string repositoryPath,
            ParsedTask parsedTask,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_analysis);
        }
    }

    private sealed class FakeAgentContextBuilder : IAgentContextBuilder
    {
        private readonly AgentContext _context;

        public FakeAgentContextBuilder(AgentContext context)
        {
            _context = context;
        }

        public Task<AgentContext> BuildAsync(
            string repositoryPath,
            ParsedTask parsedTask,
            RepositoryAnalysis repositoryAnalysis,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_context);
        }
    }

    private sealed class FakeAiCodeAgent : IAiCodeAgent
    {
        private readonly Queue<AiCodeChangeResult> _results;

        public FakeAiCodeAgent(params AiCodeChangeResult[] results)
        {
            _results = new Queue<AiCodeChangeResult>(results);
        }

        public Task<AiCodeChangeResult> GenerateChangesAsync(
            AgentContext agentContext,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_results.Dequeue());
        }

        public int FormattingRegenerationCalls { get; private set; }

        public Task<AiCodeChangeResult> RegenerateFormattedChangesAsync(
            AgentContext agentContext,
            AiCodeChangeResult previousResult,
            CancellationToken cancellationToken = default)
        {
            FormattingRegenerationCalls++;
            return Task.FromResult(_results.Dequeue());
        }

        public Task<AiCodeChangeResult> GenerateFixForTestFailureAsync(
            AgentContext agentContext,
            AiCodeChangeResult previousResult,
            BuildResult? buildResult,
            TestResult testResult,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class FakeAiChangeValidator : IAiChangeValidator
    {
        private readonly AiChangeValidationResult _result;

        public FakeAiChangeValidator(AiChangeValidationResult result)
        {
            _result = result;
        }

        public AiChangeValidationResult Validate(
            string repositoryPath,
            RepositoryAnalysis repositoryAnalysis,
            AiCodeChangeResult aiCodeChangeResult)
        {
            return _result;
        }
    }

    private sealed class SequentialAiChangeValidator : IAiChangeValidator
    {
        private readonly Queue<AiChangeValidationResult> _results;

        public SequentialAiChangeValidator(params AiChangeValidationResult[] results)
        {
            _results = new Queue<AiChangeValidationResult>(results);
        }

        public AiChangeValidationResult Validate(
            string repositoryPath,
            RepositoryAnalysis repositoryAnalysis,
            AiCodeChangeResult aiCodeChangeResult)
        {
            return _results.Dequeue();
        }
    }

    private sealed class FakeFileChangeApplier : IFileChangeApplier
    {
        private readonly IReadOnlyList<string> _changedFiles;

        public FakeFileChangeApplier(IReadOnlyList<string> changedFiles)
        {
            _changedFiles = changedFiles;
        }

        public Task<IReadOnlyList<string>> ApplyAsync(
            string repositoryPath,
            IReadOnlyList<AiChangedFile> changes,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_changedFiles);
        }
    }

    private sealed class SequentialFileChangeApplier : IFileChangeApplier
    {
        private readonly Queue<IReadOnlyList<string>> _results;

        public SequentialFileChangeApplier(params IReadOnlyList<string>[] results)
        {
            _results = new Queue<IReadOnlyList<string>>(results);
        }

        public Task<IReadOnlyList<string>> ApplyAsync(
            string repositoryPath,
            IReadOnlyList<AiChangedFile> changes,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class FakeTestRunner : ITestRunner
    {
        private readonly Queue<TestResult> _results;

        public FakeTestRunner(IEnumerable<TestResult> results)
        {
            _results = new Queue<TestResult>(results);
        }

        public int RunCalls { get; private set; }

        public Task<TestResult> RunAsync(
            string repositoryPath,
            string testCommand,
            int attemptNumber,
            CancellationToken cancellationToken = default)
        {
            RunCalls++;
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class FakeSolutionProjectSynchronizer : ISolutionProjectSynchronizer
    {
        private readonly DotNetSolutionBaseline _baseline;
        private readonly Queue<DotNetSolutionSyncResult> _syncResults;

        public FakeSolutionProjectSynchronizer(
            DotNetSolutionBaseline baseline,
            params DotNetSolutionSyncResult[] syncResults)
        {
            _baseline = baseline;
            _syncResults = new Queue<DotNetSolutionSyncResult>(syncResults);
        }

        public int CaptureCalls { get; private set; }

        public int SyncCalls { get; private set; }

        public Task<DotNetSolutionBaseline> CaptureBaselineAsync(
            string repositoryPath,
            RepositoryAnalysis repositoryAnalysis,
            CancellationToken cancellationToken = default)
        {
            CaptureCalls++;
            return Task.FromResult(_baseline);
        }

        public Task<DotNetSolutionSyncResult> SyncAsync(
            string repositoryPath,
            DotNetSolutionBaseline baseline,
            CancellationToken cancellationToken = default)
        {
            SyncCalls++;
            return Task.FromResult(_syncResults.Dequeue());
        }
    }

    private sealed class ThrowingRepositoryAnalyzer : IRepositoryAnalyzer
    {
        public Task<RepositoryAnalysis> AnalyzeAsync(
            string repositoryPath,
            ParsedTask parsedTask,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Analyzer failed.");
        }
    }

    private sealed class FakeBranchNameBuilder : IBranchNameBuilder
    {
        private readonly string _branchName;

        public FakeBranchNameBuilder(string branchName)
        {
            _branchName = branchName;
        }

        public string Build(string taskId, string title) => _branchName;
    }

    private sealed class FakePrDescriptionBuilder : IPrDescriptionBuilder
    {
        private readonly string _body;

        public FakePrDescriptionBuilder(string body)
        {
            _body = body;
        }

        public string Build(ExecutionReport report) => _body;
    }

    private sealed class FakePullRequestService : IPullRequestService
    {
        private readonly PullRequestResult _result;

        public FakePullRequestService(PullRequestResult result)
        {
            _result = result;
        }

        public bool WasCalled { get; private set; }

        public Task<PullRequestResult> CreateOrGetPullRequestAsync(
            string repositoryUrl,
            string baseBranch,
            string headBranch,
            string title,
            string body,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(_result);
        }
    }
}
