using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Application.Tasks;
using AiAgentChallenge.Domain;
using Microsoft.Extensions.Logging;

namespace AiAgentChallenge.Infrastructure.Orchestration;

public class PipelineAgentOrchestrator : IAgentOrchestrator
{
    private readonly IExecutionContextFactory _contextFactory;
    private readonly IExecutionPipelineFactory _pipelineFactory;
    private readonly ILogger<PipelineAgentOrchestrator> _logger;

    public PipelineAgentOrchestrator(
        IExecutionContextFactory contextFactory,
        IExecutionPipelineFactory pipelineFactory,
        ILogger<PipelineAgentOrchestrator> logger)
    {
        _contextFactory = contextFactory;
        _pipelineFactory = pipelineFactory;
        _logger = logger;
    }

    public async Task<ExecutionReport> StartAsync(
        CreateTaskExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = _contextFactory.Create(request);
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["TaskId"] = request.TaskId,
            ["TraceId"] = context.TraceId,
            ["ExecutionId"] = context.ExecutionId
        });

        _logger.LogInformation("Starting pipeline execution for task {TaskId}", request.TaskId);

        foreach (var step in _pipelineFactory.Create())
        {
            try
            {
                _logger.LogInformation("Starting pipeline step {Step}", step.GetType().Name);
                var result = await step.ExecuteAsync(context, cancellationToken);
                _logger.LogInformation(
                    "Completed pipeline step {Step}; shouldContinue={ShouldContinue}; stopProcessing={StopProcessing}",
                    step.GetType().Name,
                    result.ShouldContinue,
                    context.StopProcessing);

                if (!result.ShouldContinue || context.StopProcessing)
                {
                    break;
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Unhandled pipeline failure in step {Step}", step.GetType().Name);
                context.MarkFailure($"Unhandled pipeline failure in step '{step.GetType().Name}': {exception.Message}");
                break;
            }
        }

        context.FinalizeOutcome();
        _logger.LogInformation("Pipeline execution finished for task {TaskId} with status {Status}", request.TaskId, context.Status);
        return context.Report.Build(context);
    }
}
