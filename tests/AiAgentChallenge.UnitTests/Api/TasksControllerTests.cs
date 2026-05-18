using AiAgentChallenge.Api.Abstractions;
using AiAgentChallenge.Api.Controllers;
using AiAgentChallenge.Api.Models;
using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Application.Tasks;
using AiAgentChallenge.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgentChallenge.UnitTests.Api;

public sealed class TasksControllerTests
{
    [Fact]
    public async Task CreateAsync_ReturnsAcceptedExecutionReport()
    {
        var parsedTask = new ParsedTask
        {
            RepositoryUrl = "https://github.com/example-company/user-service",
            BaseBranch = "develop",
            Requirement = "Add email validation to POST /users/register endpoint.",
            AcceptanceCriteria = new[]
            {
                "Invalid email returns HTTP 400"
            }
        };

        var expectedReport = new ExecutionReport
        {
            ExecutionId = Guid.NewGuid(),
            TaskId = "TASK-123",
            Status = ExecutionStatus.Queued,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Message = "Queued",
            ParsedTask = parsedTask
        };

        var submissionService = new FakeTaskSubmissionService(TaskSubmissionResult.Success(expectedReport));
        var asyncSubmissionService = new FakeAsyncTaskSubmissionService(AsyncTaskSubmissionResult.Success(new AsyncTaskSubmissionAck
        {
            Id = Guid.NewGuid().ToString("N"),
            TaskId = "TASK-123",
            TraceId = "trace-123",
            QueuedAtUtc = DateTimeOffset.UtcNow,
            Message = "Task accepted for asynchronous processing."
        }));
        var controller = new TasksController(
            submissionService,
            asyncSubmissionService,
            new FakeExecutionReportStore(),
            new FakeTraceIdAccessor("trace-123"),
            NullLogger<TasksController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                TraceIdentifier = "trace-123"
            }
        };

        var request = new CreateTaskRequest
        {
            TaskId = "TASK-123",
            Title = "Add email validation",
            Description = """
                Repository: https://github.com/example-company/user-service
                Branch: develop

                Requirement:
                Add email validation to POST /users/register endpoint.
                """
        };

        var result = await controller.CreateAsync(request, CancellationToken.None);

        var acceptedResult = Assert.IsType<AcceptedResult>(result.Result);
        var payload = Assert.IsType<ExecutionReport>(acceptedResult.Value);
        Assert.Same(expectedReport, payload);
        Assert.NotNull(submissionService.LastRequest);
        Assert.Equal("TASK-123", submissionService.LastRequest!.TaskId);
        Assert.Equal("trace-123", submissionService.LastRequest.TraceId);
        Assert.Equal("Add email validation", submissionService.LastRequest.Title);
    }

    [Fact]
    public async Task CreateAsync_ReturnsBadRequest_WhenRequestIsInvalid()
    {
        var controller = new TasksController(
            new FakeTaskSubmissionService(TaskSubmissionResult.Failure(new Dictionary<string, string[]>
            {
                [nameof(CreateTaskRequest.TaskId)] = new[] { "TaskId cannot be empty." }
            })),
            new FakeAsyncTaskSubmissionService(AsyncTaskSubmissionResult.Success(new AsyncTaskSubmissionAck())),
            new FakeExecutionReportStore(),
            new FakeTraceIdAccessor("trace-123"),
            NullLogger<TasksController>.Instance);

        var request = new CreateTaskRequest
        {
            TaskId = " ",
            Title = "Add email validation",
            Description = "Repository: https://github.com/example-company/user-service"
        };

        var result = await controller.CreateAsync(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var details = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Contains(nameof(CreateTaskRequest.TaskId), details.Errors.Keys);
    }

    [Fact]
    public async Task CreateAsync_ReturnsBadRequest_WhenParsingFails()
    {
        var submissionService = new FakeTaskSubmissionService(TaskSubmissionResult.Failure(
            new Dictionary<string, string[]>
            {
                ["Description.Repository"] = new[] { "Repository section is required." }
            }));
        var controller = new TasksController(
            submissionService,
            new FakeAsyncTaskSubmissionService(AsyncTaskSubmissionResult.Success(new AsyncTaskSubmissionAck())),
            new FakeExecutionReportStore(),
            new FakeTraceIdAccessor("trace-123"),
            NullLogger<TasksController>.Instance);

        var request = new CreateTaskRequest
        {
            TaskId = "TASK-123",
            Title = "Add email validation",
            Description = "Requirement:\nAdd email validation"
        };

        var result = await controller.CreateAsync(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var details = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Contains("Description.Repository", details.Errors.Keys);
        Assert.NotNull(submissionService.LastRequest);
    }

    [Fact]
    public async Task CreateAsyncQueued_ReturnsAcceptedAck()
    {
        var queuedAtUtc = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid().ToString("N");
        var logger = new CapturingLogger<TasksController>();
        var controller = new TasksController(
            new FakeTaskSubmissionService(TaskSubmissionResult.Success(new ExecutionReport())),
            new FakeAsyncTaskSubmissionService(AsyncTaskSubmissionResult.Success(new AsyncTaskSubmissionAck
            {
                Id = id,
                TaskId = "TASK-123",
                TraceId = "trace-queued",
                QueuedAtUtc = queuedAtUtc,
                Message = "Task accepted for asynchronous processing."
            })),
            new FakeExecutionReportStore(),
            new FakeTraceIdAccessor("trace-queued"),
            logger);

        var request = new CreateTaskRequest
        {
            TaskId = "TASK-123",
            Title = "Add email validation",
            Description = "Repository: https://github.com/example-company/user-service"
        };

        var result = await controller.CreateAsyncQueued(request, CancellationToken.None);

        var acceptedResult = Assert.IsType<AcceptedResult>(result.Result);
        var payload = Assert.IsType<AsyncTaskAcceptedResponse>(acceptedResult.Value);
        Assert.Equal(id, payload.Id);
        Assert.Equal("TASK-123", payload.TaskId);
        Assert.Equal("trace-queued", payload.TraceId);
        Assert.Equal(queuedAtUtc, payload.QueuedAtUtc);
        Assert.Contains(logger.LogStates, state => state.TryGetValue("ExecutionId", out var value) && Equals(value, id));
    }

    [Fact]
    public async Task CreateAsyncQueued_ReturnsBadRequest_WhenRequestIsInvalid()
    {
        var controller = new TasksController(
            new FakeTaskSubmissionService(TaskSubmissionResult.Success(new ExecutionReport())),
            new FakeAsyncTaskSubmissionService(AsyncTaskSubmissionResult.ValidationFailure(new Dictionary<string, string[]>
            {
                [nameof(CreateTaskRequest.TaskId)] = new[] { "TaskId cannot be empty." }
            })),
            new FakeExecutionReportStore(),
            new FakeTraceIdAccessor("trace-queued"),
            NullLogger<TasksController>.Instance);

        var request = new CreateTaskRequest
        {
            TaskId = " ",
            Title = "Add email validation",
            Description = "Repository: https://github.com/example-company/user-service"
        };

        var result = await controller.CreateAsyncQueued(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var details = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Contains(nameof(CreateTaskRequest.TaskId), details.Errors.Keys);
    }

    [Fact]
    public async Task CreateAsyncQueued_ReturnsTooManyRequests_WhenQueueIsFull()
    {
        var controller = new TasksController(
            new FakeTaskSubmissionService(TaskSubmissionResult.Success(new ExecutionReport())),
            new FakeAsyncTaskSubmissionService(AsyncTaskSubmissionResult.QueueFull("The async execution queue is full.")),
            new FakeExecutionReportStore(),
            new FakeTraceIdAccessor("trace-queued"),
            NullLogger<TasksController>.Instance);

        var request = new CreateTaskRequest
        {
            TaskId = "TASK-123",
            Title = "Add email validation",
            Description = "Repository: https://github.com/example-company/user-service"
        };

        var result = await controller.CreateAsyncQueued(request, CancellationToken.None);

        var tooManyRequests = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, tooManyRequests.StatusCode);
        var details = Assert.IsType<ValidationProblemDetails>(tooManyRequests.Value);
        Assert.Contains("Queue", details.Errors.Keys);
    }

    [Fact]
    public async Task GetReportAsync_ReturnsOk_WhenReportExists()
    {
        var report = new ExecutionReport
        {
            ExecutionId = Guid.NewGuid(),
            TaskId = "TASK-123"
        };
        var controller = new TasksController(
            new FakeTaskSubmissionService(TaskSubmissionResult.Success(new ExecutionReport())),
            new FakeAsyncTaskSubmissionService(AsyncTaskSubmissionResult.Success(new AsyncTaskSubmissionAck())),
            new FakeExecutionReportStore(report),
            new FakeTraceIdAccessor("trace-queued"),
            NullLogger<TasksController>.Instance);

        var result = await controller.GetReportAsync(report.ExecutionId.ToString("N"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(report, ok.Value);
    }

    [Fact]
    public async Task GetReportAsync_ReturnsNotFound_WhenReportDoesNotExist()
    {
        var controller = new TasksController(
            new FakeTaskSubmissionService(TaskSubmissionResult.Success(new ExecutionReport())),
            new FakeAsyncTaskSubmissionService(AsyncTaskSubmissionResult.Success(new AsyncTaskSubmissionAck())),
            new FakeExecutionReportStore(),
            new FakeTraceIdAccessor("trace-queued"),
            NullLogger<TasksController>.Instance);

        var result = await controller.GetReportAsync(Guid.NewGuid().ToString("N"), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    private sealed class FakeTaskSubmissionService : ITaskSubmissionService
    {
        private readonly TaskSubmissionResult _result;

        public FakeTaskSubmissionService(TaskSubmissionResult result)
        {
            _result = result;
        }

        public TaskSubmissionRequest? LastRequest { get; private set; }

        public Task<TaskSubmissionResult> SubmitAsync(
            TaskSubmissionRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeTraceIdAccessor : ITraceIdAccessor
    {
        private readonly string _traceId;

        public FakeTraceIdAccessor(string traceId)
        {
            _traceId = traceId;
        }

        public string GetOrCreate(HttpContext httpContext)
        {
            return _traceId;
        }
    }

    private sealed class FakeAsyncTaskSubmissionService : IAsyncTaskSubmissionService
    {
        private readonly AsyncTaskSubmissionResult _result;

        public FakeAsyncTaskSubmissionService(AsyncTaskSubmissionResult result)
        {
            _result = result;
        }

        public TaskSubmissionRequest? LastRequest { get; private set; }

        public Task<AsyncTaskSubmissionResult> EnqueueAsync(
            TaskSubmissionRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeExecutionReportStore : IExecutionReportStore
    {
        private readonly ExecutionReport? _report;

        public FakeExecutionReportStore(ExecutionReport? report = null)
        {
            _report = report;
        }

        public Task<ExecutionReport?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_report);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<IReadOnlyDictionary<string, object?>> LogStates { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
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
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                LogStates.Add(pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase));
            }
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
