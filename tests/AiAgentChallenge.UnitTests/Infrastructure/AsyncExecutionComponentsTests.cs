using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Application.Tasks;
using AiAgentChallenge.Domain;
using AiAgentChallenge.Infrastructure.Orchestration;
using AiAgentChallenge.Infrastructure.Paths;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class AsyncExecutionComponentsTests
{
    [Fact]
    public async Task TaskSubmissionService_ExportsExecutionReport_OnSuccess()
    {
        var report = new ExecutionReport
        {
            ExecutionId = Guid.NewGuid(),
            TaskId = "TASK-123",
            Status = ExecutionStatus.Completed,
            CreatedAtUtc = new DateTimeOffset(2026, 05, 17, 10, 30, 15, TimeSpan.Zero),
            Message = "Done"
        };
        var exporter = new CapturingExecutionReportExporter();
        var service = new TaskSubmissionService(
            new PassThroughRequestValidator(),
            new SuccessfulRequestFactory(),
            new SuccessfulAgentOrchestrator(report),
            exporter,
            NullLogger<TaskSubmissionService>.Instance);

        var result = await service.SubmitAsync(new TaskSubmissionRequest
        {
            ExecutionId = report.ExecutionId,
            TaskId = "TASK-123",
            TraceId = "trace-123",
            Title = "Add email validation",
            Description = "Repository: https://github.com/example-company/user-service"
        });

        Assert.True(result.IsSuccess);
        Assert.Same(report, result.Report);
        Assert.Same(report, exporter.LastReport);
        Assert.Equal("TASK-123", exporter.LastTaskTitle);
    }

    [Fact]
    public async Task TaskSubmissionService_SwallowsExecutionReportExportFailures()
    {
        var report = new ExecutionReport
        {
            ExecutionId = Guid.NewGuid(),
            TaskId = "TASK-123",
            Status = ExecutionStatus.Completed,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Message = "Done"
        };
        var service = new TaskSubmissionService(
            new PassThroughRequestValidator(),
            new SuccessfulRequestFactory(),
            new SuccessfulAgentOrchestrator(report),
            new ThrowingExecutionReportExporter(),
            NullLogger<TaskSubmissionService>.Instance);

        var result = await service.SubmitAsync(new TaskSubmissionRequest
        {
            ExecutionId = report.ExecutionId,
            TaskId = "TASK-123",
            TraceId = "trace-123",
            Title = "Add email validation",
            Description = "Repository: https://github.com/example-company/user-service"
        });

        Assert.True(result.IsSuccess);
        Assert.Same(report, result.Report);
    }

    [Fact]
    public async Task TaskSubmissionService_AddsExecutionIdTaskIdAndTraceIdToLogScope()
    {
        var report = new ExecutionReport
        {
            ExecutionId = Guid.NewGuid(),
            TaskId = "TASK-123",
            Status = ExecutionStatus.Completed,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Message = "Done"
        };
        var logger = new ScopeCapturingLogger<TaskSubmissionService>();
        var service = new TaskSubmissionService(
            new PassThroughRequestValidator(),
            new SuccessfulRequestFactory(),
            new SuccessfulAgentOrchestrator(report),
            new CapturingExecutionReportExporter(),
            logger);

        var executionId = Guid.NewGuid();
        await service.SubmitAsync(new TaskSubmissionRequest
        {
            ExecutionId = executionId,
            TaskId = "TASK-123",
            TraceId = "trace-123",
            Title = "Add email validation",
            Description = "Repository: https://github.com/example-company/user-service"
        });

        var scope = Assert.Single(logger.ScopeStates);
        Assert.Equal(executionId, scope["ExecutionId"]);
        Assert.Equal("TASK-123", scope["TaskId"]);
        Assert.Equal("trace-123", scope["TraceId"]);
    }

    [Fact]
    public async Task TaskExecutionRequestFactory_AddsExecutionIdTaskIdAndTraceIdToLogScope()
    {
        var logger = new ScopeCapturingLogger<TaskExecutionRequestFactory>();
        var factory = new TaskExecutionRequestFactory(
            new SuccessfulTaskParser(new ParsedTask
            {
                RepositoryUrl = "https://github.com/example-company/user-service",
                BaseBranch = "main",
                Requirement = "Add email validation"
            }),
            logger);

        var executionId = Guid.NewGuid();
        var result = await factory.CreateAsync(new TaskSubmissionRequest
        {
            ExecutionId = executionId,
            TaskId = "TASK-123",
            TraceId = "trace-123",
            Title = "Add email validation",
            Description = "Repository: https://github.com/example-company/user-service"
        });

        Assert.True(result.IsSuccess);
        var scope = Assert.Single(logger.ScopeStates);
        Assert.Equal(executionId, scope["ExecutionId"]);
        Assert.Equal("TASK-123", scope["TaskId"]);
        Assert.Equal("trace-123", scope["TraceId"]);
    }

    [Fact]
    public async Task FileExecutionReportExporter_WritesJsonFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "execution-report-export-tests", Guid.NewGuid().ToString("N"));
        var exporter = new FileExecutionReportExporter(Options.Create(new ExecutionReportStorageOptions
        {
            Directory = directory
        }));
        var report = new ExecutionReport
        {
            ExecutionId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            TaskId = "TASK-123",
            Status = ExecutionStatus.Completed,
            CreatedAtUtc = new DateTimeOffset(2026, 05, 17, 10, 30, 15, TimeSpan.Zero),
            Message = "Done"
        };

        try
        {
            await exporter.ExportAsync(report, "TASK-123");

            var files = Directory.GetFiles(directory, "*.json");
            var file = Assert.Single(files);
            Assert.Equal("ExecutionReport_TASK-123_11111111222233334444555555555555.json", Path.GetFileName(file));
            var content = await File.ReadAllTextAsync(file);
            Assert.Contains("\"taskId\": \"TASK-123\"", content);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AsyncTaskExecutionBackgroundService_ProcessesQueuedRequests()
    {
        var queue = new ChannelAsyncExecutionQueue(Options.Create(new AsyncExecutionOptions
        {
            QueueCapacity = 4
        }));
        var submissionService = new CapturingTaskSubmissionService();
        var services = new ServiceCollection();
        services.AddSingleton<ITaskSubmissionService>(submissionService);
        await using var serviceProvider = services.BuildServiceProvider();
        var worker = new AsyncTaskExecutionBackgroundService(
            queue,
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AsyncTaskExecutionBackgroundService>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            var enqueued = queue.TryEnqueue(new QueuedTaskSubmission
            {
                ExecutionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                TaskId = "TASK-123",
                TraceId = "trace-123",
                Title = "Add email validation",
                Description = "Repository: https://github.com/example-company/user-service",
                QueuedAtUtc = DateTimeOffset.UtcNow
            });

            Assert.True(enqueued);
            await submissionService.Completion.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotNull(submissionService.LastRequest);
            Assert.Equal(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), submissionService.LastRequest!.ExecutionId);
            Assert.Equal("TASK-123", submissionService.LastRequest!.TaskId);
            Assert.Equal("Add email validation", submissionService.LastRequest.Title);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    [Fact]
    public async Task AsyncTaskSubmissionService_AddsEffectiveExecutionIdToLogScope()
    {
        var logger = new ScopeCapturingLogger<AsyncTaskSubmissionService>();
        var service = new AsyncTaskSubmissionService(
            new PassThroughRequestValidator(),
            new ChannelAsyncExecutionQueue(Options.Create(new AsyncExecutionOptions { QueueCapacity = 1 })),
            logger);

        var result = await service.EnqueueAsync(new TaskSubmissionRequest
        {
            ExecutionId = Guid.Empty,
            TaskId = "TASK-123",
            TraceId = "trace-123",
            Title = "Add email validation",
            Description = "Repository: https://github.com/example-company/user-service"
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Ack);
        var scope = Assert.Single(logger.ScopeStates);
        Assert.Equal(Guid.ParseExact(result.Ack!.Id, "N"), scope["ExecutionId"]);
        Assert.Equal("TASK-123", scope["TaskId"]);
        Assert.Equal("trace-123", scope["TraceId"]);
    }

    [Fact]
    public async Task AsyncTaskExecutionBackgroundService_AddsExecutionIdToLogScope()
    {
        var queue = new ChannelAsyncExecutionQueue(Options.Create(new AsyncExecutionOptions
        {
            QueueCapacity = 4
        }));
        var submissionService = new CapturingTaskSubmissionService();
        var logger = new ScopeCapturingLogger<AsyncTaskExecutionBackgroundService>();
        var services = new ServiceCollection();
        services.AddSingleton<ITaskSubmissionService>(submissionService);
        await using var serviceProvider = services.BuildServiceProvider();
        var worker = new AsyncTaskExecutionBackgroundService(
            queue,
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            logger);

        var executionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var enqueued = queue.TryEnqueue(new QueuedTaskSubmission
            {
                ExecutionId = executionId,
                TaskId = "TASK-123",
                TraceId = "trace-123",
                Title = "Add email validation",
                Description = "Repository: https://github.com/example-company/user-service",
                QueuedAtUtc = DateTimeOffset.UtcNow
            });

            Assert.True(enqueued);
            await submissionService.Completion.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var scope = Assert.Single(logger.ScopeStates);
            Assert.Equal(executionId, scope["ExecutionId"]);
            Assert.Equal("TASK-123", scope["TaskId"]);
            Assert.Equal("trace-123", scope["TraceId"]);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    [Fact]
    public async Task FileExecutionReportStore_ReturnsReport_WhenIdExists()
    {
        var directory = Path.Combine(Path.GetTempPath(), "execution-report-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var report = new ExecutionReport
        {
            ExecutionId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            TaskId = "TASK-123",
            Status = ExecutionStatus.Completed,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Message = "Done"
        };
        var exporter = new FileExecutionReportExporter(Options.Create(new ExecutionReportStorageOptions
        {
            Directory = directory
        }));
        var store = new FileExecutionReportStore(Options.Create(new ExecutionReportStorageOptions
        {
            Directory = directory
        }));

        try
        {
            await exporter.ExportAsync(report, "TASK-123");

            var loaded = await store.FindByIdAsync("11111111222233334444555555555555");

            Assert.NotNull(loaded);
            Assert.Equal(report.ExecutionId, loaded!.ExecutionId);
            Assert.Equal("TASK-123", loaded.TaskId);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FileExecutionReportStore_ReturnsNull_WhenIdDoesNotExist()
    {
        var directory = Path.Combine(Path.GetTempPath(), "execution-report-store-tests", Guid.NewGuid().ToString("N"));
        var store = new FileExecutionReportStore(Options.Create(new ExecutionReportStorageOptions
        {
            Directory = directory
        }));

        var loaded = await store.FindByIdAsync(Guid.NewGuid().ToString("N"));

        Assert.Null(loaded);
    }

    [Fact]
    public void FileExecutionReportExporter_NormalizeDirectory_ResolvesRelativePathsFromApplicationBase()
    {
        var normalized = FileExecutionReportExporter.NormalizeDirectory("execution-reports");

        Assert.Equal(
            Path.GetFullPath(Path.Combine(ApplicationPathResolver.GetApplicationBaseDirectory(), "execution-reports")),
            normalized);
    }

    private sealed class PassThroughRequestValidator : ITaskSubmissionRequestValidator
    {
        public IReadOnlyDictionary<string, string[]> Validate(TaskSubmissionRequest request)
        {
            return new Dictionary<string, string[]>();
        }
    }

    private sealed class SuccessfulRequestFactory : ITaskExecutionRequestFactory
    {
        public Task<TaskExecutionRequestFactoryResult> CreateAsync(
            TaskSubmissionRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(TaskExecutionRequestFactoryResult.Success(new CreateTaskExecutionRequest
            {
                ExecutionId = request.ExecutionId,
                TaskId = request.TaskId,
                TraceId = request.TraceId,
                Title = request.Title,
                Description = request.Description,
                ParsedTask = new ParsedTask()
            }));
        }
    }

    private sealed class SuccessfulTaskParser : ITaskParser
    {
        private readonly ParsedTask _parsedTask;

        public SuccessfulTaskParser(ParsedTask parsedTask)
        {
            _parsedTask = parsedTask;
        }

        public Task<TaskParseResult> ParseAsync(string description, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(TaskParseResult.Success(_parsedTask));
        }
    }

    private sealed class SuccessfulAgentOrchestrator : IAgentOrchestrator
    {
        private readonly ExecutionReport _report;

        public SuccessfulAgentOrchestrator(ExecutionReport report)
        {
            _report = report;
        }

        public Task<ExecutionReport> StartAsync(
            CreateTaskExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_report);
        }
    }

    private sealed class CapturingExecutionReportExporter : IExecutionReportExporter
    {
        public ExecutionReport? LastReport { get; private set; }

        public string? LastTaskTitle { get; private set; }

        public Task ExportAsync(
            ExecutionReport report,
            string taskTitle,
            CancellationToken cancellationToken = default)
        {
            LastReport = report;
            LastTaskTitle = taskTitle;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingExecutionReportExporter : IExecutionReportExporter
    {
        public Task ExportAsync(
            ExecutionReport report,
            string taskTitle,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class CapturingTaskSubmissionService : ITaskSubmissionService
    {
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskSubmissionRequest? LastRequest { get; private set; }

        public Task<TaskSubmissionResult> SubmitAsync(
            TaskSubmissionRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            Completion.TrySetResult(true);
            return Task.FromResult(TaskSubmissionResult.Success(new ExecutionReport
            {
                ExecutionId = request.ExecutionId == Guid.Empty ? Guid.NewGuid() : request.ExecutionId,
                TaskId = request.TaskId,
                Status = ExecutionStatus.Completed,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Message = "Done"
            }));
        }
    }

    private sealed class ScopeCapturingLogger<T> : ILogger<T>
    {
        public List<IReadOnlyDictionary<string, object?>> ScopeStates { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            ScopeStates.Add(ToDictionary(state));
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }

        private static IReadOnlyDictionary<string, object?> ToDictionary<TState>(TState state)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                return pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            }

            if (state is IEnumerable<KeyValuePair<string, object>> nonNullablePairs)
            {
                return nonNullablePairs.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.OrdinalIgnoreCase);
            }

            throw new InvalidOperationException("Logger scope state was not structured as key/value pairs.");
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
