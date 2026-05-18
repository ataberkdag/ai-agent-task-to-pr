using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Infrastructure.Ai;
using AiAgentChallenge.Infrastructure.Projects;
using AiAgentChallenge.Infrastructure.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.Infrastructure.Orchestration;

public sealed class StubAgentOrchestrator : PipelineAgentOrchestrator
{
    public StubAgentOrchestrator(
        IRepositoryPolicy repositoryPolicy,
        IWorkspaceService workspaceService,
        IGitClient gitClient,
        IRepositoryAnalyzer repositoryAnalyzer,
        IAgentContextBuilder agentContextBuilder,
        IAiCodeAgent aiCodeAgent,
        IAiChangeValidator aiChangeValidator,
        IFileChangeApplier fileChangeApplier,
        ITestRunner testRunner,
        IBranchNameBuilder branchNameBuilder,
        IPrDescriptionBuilder prDescriptionBuilder,
        IPullRequestService pullRequestService,
        IOptions<AiOptions> aiOptions,
        ILogger<StubAgentOrchestrator> logger,
        ISolutionProjectSynchronizer? solutionProjectSynchronizer = null,
        IBuildRunner? buildRunner = null)
        : base(
            new ExecutionContextFactory(),
            new ExecutionPipelineFactory(new IExecutionStep[]
            {
                new RepositoryPolicyValidationStep(repositoryPolicy),
                new WorkspaceCreationStep(workspaceService),
                new RepositoryCloneStep(gitClient),
                new RepositoryAnalysisStep(repositoryAnalyzer),
                new SolutionBaselineCaptureStep(solutionProjectSynchronizer ?? new NoOpSolutionProjectSynchronizer()),
                new AgentContextBuildStep(agentContextBuilder),
                new AiChangePreparationStep(
                    aiCodeAgent,
                    aiChangeValidator,
                    new NeedsFormattingRegenerationRule()),
                new ApplyChangesAndSyncStep(fileChangeApplier, solutionProjectSynchronizer ?? new NoOpSolutionProjectSynchronizer()),
                new TestAndFixWorkflowStep(
                    buildRunner ?? new NoOpBuildRunner(),
                    testRunner,
                    aiCodeAgent,
                    aiChangeValidator,
                    fileChangeApplier,
                    solutionProjectSynchronizer ?? new NoOpSolutionProjectSynchronizer(),
                    new NeedsAiFixRule(aiOptions),
                    NullLogger<TestAndFixWorkflowStep>.Instance),
                new GitFinalizationStep(
                    gitClient,
                    branchNameBuilder,
                    prDescriptionBuilder,
                    pullRequestService,
                    new CanRunGitFinalizationRule())
            }),
            logger as ILogger<PipelineAgentOrchestrator> ?? NullLogger<PipelineAgentOrchestrator>.Instance)
    {
    }
}
