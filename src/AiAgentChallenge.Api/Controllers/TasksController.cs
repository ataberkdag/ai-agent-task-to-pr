using AiAgentChallenge.Api.Abstractions;
using AiAgentChallenge.Api.Models;
using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Application.Tasks;
using AiAgentChallenge.Domain;
using Microsoft.AspNetCore.Mvc;

namespace AiAgentChallenge.Api.Controllers;

[ApiController]
[Route("api/tasks")]
public sealed class TasksController : ControllerBase
{
    private readonly ITaskSubmissionService _taskSubmissionService;
    private readonly IAsyncTaskSubmissionService _asyncTaskSubmissionService;
    private readonly IExecutionReportStore _executionReportStore;
    private readonly ITraceIdAccessor _traceIdAccessor;
    private readonly ILogger<TasksController> _logger;

    public TasksController(
        ITaskSubmissionService taskSubmissionService,
        IAsyncTaskSubmissionService asyncTaskSubmissionService,
        IExecutionReportStore executionReportStore,
        ITraceIdAccessor traceIdAccessor,
        ILogger<TasksController> logger)
    {
        _taskSubmissionService = taskSubmissionService;
        _asyncTaskSubmissionService = asyncTaskSubmissionService;
        _executionReportStore = executionReportStore;
        _traceIdAccessor = traceIdAccessor;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(ExecutionReport), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ExecutionReport>> CreateAsync(
        [FromBody] CreateTaskRequest request,
        CancellationToken cancellationToken)
    {
        var submissionResult = await _taskSubmissionService.SubmitAsync(
            new TaskSubmissionRequest
            {
                ExecutionId = Guid.NewGuid(),
                TaskId = request.TaskId,
                TraceId = _traceIdAccessor.GetOrCreate(HttpContext),
                Title = request.Title,
                Description = request.Description
            },
            cancellationToken);

        if (!submissionResult.IsSuccess || submissionResult.Report is null)
        {
            return CreateValidationProblem(submissionResult.Errors);
        }

        var report = submissionResult.Report;

        _logger.LogInformation(
            "Accepted task {TaskId} with execution {ExecutionId} and trace {TraceId}",
            report.TaskId,
            report.ExecutionId,
            report.TraceId);

        return Accepted(report);
    }

    [HttpPost("async")]
    [ProducesResponseType(typeof(AsyncTaskAcceptedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AsyncTaskAcceptedResponse>> CreateAsyncQueued(
        [FromBody] CreateTaskRequest request,
        CancellationToken cancellationToken)
    {
        var submissionResult = await _asyncTaskSubmissionService.EnqueueAsync(
            new TaskSubmissionRequest
            {
                ExecutionId = Guid.NewGuid(),
                TaskId = request.TaskId,
                TraceId = _traceIdAccessor.GetOrCreate(HttpContext),
                Title = request.Title,
                Description = request.Description
            },
            cancellationToken);

        if (!submissionResult.IsSuccess || submissionResult.Ack is null)
        {
            if (submissionResult.IsQueueFull)
            {
                return StatusCode(
                    StatusCodes.Status429TooManyRequests,
                    new ValidationProblemDetails(new Dictionary<string, string[]>(submissionResult.Errors))
                    {
                        Title = "The async execution queue is full.",
                        Status = StatusCodes.Status429TooManyRequests
                    });
            }

            return CreateValidationProblem(submissionResult.Errors);
        }

        var ack = submissionResult.Ack;
        _logger.LogInformation(
            "Accepted async task {TaskId} into queue with execution {ExecutionId} and trace {TraceId} at {QueuedAtUtc}",
            ack.TaskId,
            ack.Id,
            ack.TraceId,
            ack.QueuedAtUtc);

        return Accepted(new AsyncTaskAcceptedResponse
        {
            Id = ack.Id,
            TaskId = ack.TaskId,
            TraceId = ack.TraceId,
            QueuedAtUtc = ack.QueuedAtUtc,
            Message = ack.Message
        });
    }

    [HttpGet("reports/{id}")]
    [ProducesResponseType(typeof(ExecutionReport), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExecutionReport>> GetReportAsync(
        [FromRoute] string id,
        CancellationToken cancellationToken)
    {
        var report = await _executionReportStore.FindByIdAsync(id, cancellationToken);
        if (report is null)
        {
            return NotFound();
        }

        return Ok(report);
    }

    private BadRequestObjectResult CreateValidationProblem(IReadOnlyDictionary<string, string[]> errors)
    {
        return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>(errors))
        {
            Title = "One or more validation errors occurred.",
            Status = StatusCodes.Status400BadRequest
        });
    }
}
