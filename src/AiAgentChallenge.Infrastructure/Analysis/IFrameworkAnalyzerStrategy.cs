namespace AiAgentChallenge.Infrastructure.Analysis;

internal interface IFrameworkAnalyzerStrategy
{
    bool CanHandle(ProjectDetection detection);

    Task<FrameworkAnalysisResult> AnalyzeAsync(
        RepositoryScanContext context,
        CancellationToken cancellationToken = default);
}
