using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Application.Tasks;
using AiAgentChallenge.Domain;
using AiAgentChallenge.Infrastructure.Paths;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.Infrastructure.Orchestration;

internal sealed class AsyncExecutionOptions
{
    public int QueueCapacity { get; set; } = 100;
}

internal sealed class ExecutionReportStorageOptions
{
    public string Directory { get; set; } = "execution-reports";
}

internal sealed class QueuedTaskSubmission
{
    public Guid ExecutionId { get; init; }

    public string TaskId { get; init; } = string.Empty;

    public string TraceId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public DateTimeOffset QueuedAtUtc { get; init; }
}

internal interface IAsyncExecutionQueue
{
    bool TryEnqueue(QueuedTaskSubmission submission);

    IAsyncEnumerable<QueuedTaskSubmission> ReadAllAsync(CancellationToken cancellationToken);
}

internal interface IExecutionReportExporter
{
    Task ExportAsync(
        ExecutionReport report,
        string taskId,
        CancellationToken cancellationToken = default);
}

internal sealed class ChannelAsyncExecutionQueue : IAsyncExecutionQueue
{
    private readonly Channel<QueuedTaskSubmission> _channel;

    public ChannelAsyncExecutionQueue(IOptions<AsyncExecutionOptions> options)
    {
        var capacity = Math.Max(1, options.Value.QueueCapacity);
        _channel = Channel.CreateBounded<QueuedTaskSubmission>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public bool TryEnqueue(QueuedTaskSubmission submission)
    {
        return _channel.Writer.TryWrite(submission);
    }

    public IAsyncEnumerable<QueuedTaskSubmission> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}

internal sealed class FileExecutionReportExporter : IExecutionReportExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _directory;

    public FileExecutionReportExporter(IOptions<ExecutionReportStorageOptions> options)
    {
        _directory = NormalizeDirectory(options.Value.Directory);
    }

    public async Task ExportAsync(
        ExecutionReport report,
        string taskId,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);

        var fileName = BuildFileName(taskId, report.ExecutionId);
        var path = Path.Combine(_directory, fileName);
        var json = JsonSerializer.Serialize(report, JsonOptions);

        await File.WriteAllTextAsync(path, json, Encoding.UTF8, cancellationToken);
    }

    internal static string NormalizeDirectory(string? directory)
    {
        return ApplicationPathResolver.ResolveAgainstApplicationBase(directory, "execution-reports");
    }

    internal static string BuildFileName(string? taskId, Guid executionId)
    {
        var safeTaskId = Slugify(taskId);
        return $"ExecutionReport_{safeTaskId}_{executionId:N}.json";
    }

    private static string Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "task";
        }

        var builder = new StringBuilder(value.Length);
        var previousDash = false;
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousDash = false;
                continue;
            }

            if (!previousDash)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        var sanitized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "task" : sanitized;
    }
}

internal sealed class FileExecutionReportStore : IExecutionReportStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _directory;

    public FileExecutionReportStore(IOptions<ExecutionReportStorageOptions> options)
    {
        _directory = FileExecutionReportExporter.NormalizeDirectory(options.Value.Directory);
    }

    public async Task<ExecutionReport?> FindByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id) || !Guid.TryParseExact(id, "N", out _))
        {
            return null;
        }

        if (!Directory.Exists(_directory))
        {
            return null;
        }

        var pattern = $"ExecutionReport_*_{id}.json";
        var path = Directory.EnumerateFiles(_directory, pattern, SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (path is null)
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ExecutionReport>(stream, JsonOptions, cancellationToken);
    }
}

internal sealed class AsyncTaskSubmissionService : IAsyncTaskSubmissionService
{
    private readonly ITaskSubmissionRequestValidator _requestValidator;
    private readonly IAsyncExecutionQueue _queue;
    private readonly ILogger<AsyncTaskSubmissionService> _logger;

    public AsyncTaskSubmissionService(
        ITaskSubmissionRequestValidator requestValidator,
        IAsyncExecutionQueue queue,
        ILogger<AsyncTaskSubmissionService> logger)
    {
        _requestValidator = requestValidator;
        _queue = queue;
        _logger = logger;
    }

    public Task<AsyncTaskSubmissionResult> EnqueueAsync(
        TaskSubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        var executionId = request.ExecutionId == Guid.Empty ? Guid.NewGuid() : request.ExecutionId;
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["TaskId"] = request.TaskId,
            ["TraceId"] = request.TraceId,
            ["ExecutionId"] = executionId
        });

        _logger.LogInformation("Async task submission received for task {TaskId}", request.TaskId);
        var validationErrors = _requestValidator.Validate(request);
        if (validationErrors.Count > 0)
        {
            _logger.LogError("Async task submission validation failed for task {TaskId}", request.TaskId);
            return Task.FromResult(AsyncTaskSubmissionResult.ValidationFailure(validationErrors));
        }

        var submission = new QueuedTaskSubmission
        {
            ExecutionId = executionId,
            TaskId = request.TaskId,
            TraceId = request.TraceId,
            Title = request.Title,
            Description = request.Description,
            QueuedAtUtc = DateTimeOffset.UtcNow
        };

        if (!_queue.TryEnqueue(submission))
        {
            _logger.LogWarning("Async execution queue is full for task {TaskId}", request.TaskId);
            return Task.FromResult(AsyncTaskSubmissionResult.QueueFull("The async execution queue is full."));
        }

        _logger.LogInformation("Async task {TaskId} enqueued successfully", request.TaskId);
        return Task.FromResult(AsyncTaskSubmissionResult.Success(new AsyncTaskSubmissionAck
        {
            Id = submission.ExecutionId.ToString("N"),
            TaskId = submission.TaskId,
            TraceId = submission.TraceId,
            QueuedAtUtc = submission.QueuedAtUtc,
            Message = "Task accepted for asynchronous processing."
        }));
    }
}

internal sealed class AsyncTaskExecutionBackgroundService : BackgroundService
{
    private readonly IAsyncExecutionQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AsyncTaskExecutionBackgroundService> _logger;

    public AsyncTaskExecutionBackgroundService(
        IAsyncExecutionQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<AsyncTaskExecutionBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var submission in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var logScope = _logger.BeginScope(new Dictionary<string, object>
                {
                    ["TaskId"] = submission.TaskId,
                    ["TraceId"] = submission.TraceId,
                    ["ExecutionId"] = submission.ExecutionId
                });

                using var scope = _scopeFactory.CreateScope();
                var taskSubmissionService = scope.ServiceProvider.GetRequiredService<ITaskSubmissionService>();

                _logger.LogInformation(
                    "Processing queued async task {TaskId} queued at {QueuedAtUtc}",
                    submission.TaskId,
                    submission.QueuedAtUtc);

                var result = await taskSubmissionService.SubmitAsync(
                    new TaskSubmissionRequest
                    {
                        ExecutionId = submission.ExecutionId,
                        TaskId = submission.TaskId,
                        TraceId = submission.TraceId,
                        Title = submission.Title,
                        Description = submission.Description
                    },
                    stoppingToken);

                if (!result.IsSuccess || result.Report is null)
                {
                    _logger.LogError("Queued async task {TaskId} failed before report generation", submission.TaskId);
                    continue;
                }

                _logger.LogInformation(
                    "Queued async task {TaskId} completed with status {Status}",
                    submission.TaskId,
                    result.Report.Status);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Unhandled queued async task failure for task {TaskId}", submission.TaskId);
            }
        }
    }
}
