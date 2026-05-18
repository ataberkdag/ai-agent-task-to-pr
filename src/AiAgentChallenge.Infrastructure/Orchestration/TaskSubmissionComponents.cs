using System.ComponentModel.DataAnnotations;
using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Application.Tasks;
using AiAgentChallenge.Domain;
using Microsoft.Extensions.Logging;

namespace AiAgentChallenge.Infrastructure.Orchestration;

internal interface ITaskSubmissionRequestValidator
{
    IReadOnlyDictionary<string, string[]> Validate(TaskSubmissionRequest request);
}

internal interface ITaskExecutionRequestFactory
{
    Task<TaskExecutionRequestFactoryResult> CreateAsync(
        TaskSubmissionRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed class TaskSubmissionRequestValidator : ITaskSubmissionRequestValidator
{
    public IReadOnlyDictionary<string, string[]> Validate(TaskSubmissionRequest request)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        AddIfBlank(nameof(TaskSubmissionRequest.TaskId), request.TaskId, "TaskId cannot be empty.");
        AddIfBlank(nameof(TaskSubmissionRequest.Title), request.Title, "Title cannot be empty.");
        AddIfBlank(nameof(TaskSubmissionRequest.Description), request.Description, "Description cannot be empty.");

        return errors.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);

        void AddIfBlank(string key, string value, string message)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (!errors.TryGetValue(key, out var messages))
            {
                messages = new List<string>();
                errors[key] = messages;
            }

            messages.Add(message);
        }
    }
}

internal sealed class TaskExecutionRequestFactory : ITaskExecutionRequestFactory
{
    private readonly ITaskParser _taskParser;
    private readonly ILogger<TaskExecutionRequestFactory> _logger;

    public TaskExecutionRequestFactory(
        ITaskParser taskParser,
        ILogger<TaskExecutionRequestFactory> logger)
    {
        _taskParser = taskParser;
        _logger = logger;
    }

    public async Task<TaskExecutionRequestFactoryResult> CreateAsync(
        TaskSubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["TaskId"] = request.TaskId,
            ["TraceId"] = request.TraceId,
            ["ExecutionId"] = request.ExecutionId
        });

        _logger.LogInformation("Parsing task description for task {TaskId}", request.TaskId);
        var parseResult = await _taskParser.ParseAsync(request.Description, cancellationToken);
        if (!parseResult.IsSuccess)
        {
            _logger.LogError("Task parsing failed for task {TaskId}", request.TaskId);
            return TaskExecutionRequestFactoryResult.Failure(parseResult.Errors);
        }

        _logger.LogInformation("Task description parsed successfully for task {TaskId}", request.TaskId);

        return TaskExecutionRequestFactoryResult.Success(new CreateTaskExecutionRequest
        {
            ExecutionId = request.ExecutionId,
            TaskId = request.TaskId,
            TraceId = request.TraceId,
            Title = request.Title,
            Description = request.Description,
            ParsedTask = parseResult.ParsedTask ?? new ParsedTask()
        });
    }
}

internal sealed class TaskSubmissionService : ITaskSubmissionService
{
    private readonly ITaskSubmissionRequestValidator _requestValidator;
    private readonly ITaskExecutionRequestFactory _requestFactory;
    private readonly IAgentOrchestrator _agentOrchestrator;
    private readonly IExecutionReportExporter _reportExporter;
    private readonly ILogger<TaskSubmissionService> _logger;

    public TaskSubmissionService(
        ITaskSubmissionRequestValidator requestValidator,
        ITaskExecutionRequestFactory requestFactory,
        IAgentOrchestrator agentOrchestrator,
        IExecutionReportExporter reportExporter,
        ILogger<TaskSubmissionService> logger)
    {
        _requestValidator = requestValidator;
        _requestFactory = requestFactory;
        _agentOrchestrator = agentOrchestrator;
        _reportExporter = reportExporter;
        _logger = logger;
    }

    public async Task<TaskSubmissionResult> SubmitAsync(
        TaskSubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["TaskId"] = request.TaskId,
            ["TraceId"] = request.TraceId,
            ["ExecutionId"] = request.ExecutionId
        });

        _logger.LogInformation("Task submission received for task {TaskId}", request.TaskId);
        var validationErrors = _requestValidator.Validate(request);
        if (validationErrors.Count > 0)
        {
            _logger.LogError("Task submission validation failed for task {TaskId}", request.TaskId);
            return TaskSubmissionResult.Failure(validationErrors);
        }

        var executionRequestResult = await _requestFactory.CreateAsync(request, cancellationToken);
        if (!executionRequestResult.IsSuccess || executionRequestResult.ExecutionRequest is null)
        {
            _logger.LogError("Task execution request creation failed for task {TaskId}", request.TaskId);
            return TaskSubmissionResult.Failure(executionRequestResult.Errors);
        }

        var report = await _agentOrchestrator.StartAsync(executionRequestResult.ExecutionRequest, cancellationToken);
        try
        {
            await _reportExporter.ExportAsync(report, request.TaskId, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Execution report export failed for task {TaskId}", request.TaskId);
        }

        _logger.LogInformation("Task submission completed for task {TaskId} with status {Status}", request.TaskId, report.Status);
        return TaskSubmissionResult.Success(report);
    }
}

internal sealed class TaskExecutionRequestFactoryResult
{
    private TaskExecutionRequestFactoryResult(
        bool isSuccess,
        CreateTaskExecutionRequest? executionRequest,
        IReadOnlyDictionary<string, string[]> errors)
    {
        IsSuccess = isSuccess;
        ExecutionRequest = executionRequest;
        Errors = errors;
    }

    public bool IsSuccess { get; }

    public CreateTaskExecutionRequest? ExecutionRequest { get; }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public static TaskExecutionRequestFactoryResult Success(CreateTaskExecutionRequest executionRequest)
    {
        return new TaskExecutionRequestFactoryResult(true, executionRequest, new Dictionary<string, string[]>());
    }

    public static TaskExecutionRequestFactoryResult Failure(IReadOnlyDictionary<string, string[]> errors)
    {
        return new TaskExecutionRequestFactoryResult(false, null, errors);
    }
}
