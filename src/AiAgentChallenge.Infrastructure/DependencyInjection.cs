using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Infrastructure.Analysis;
using AiAgentChallenge.Infrastructure.Ai;
using AiAgentChallenge.Infrastructure.Files;
using AiAgentChallenge.Infrastructure.Git;
using AiAgentChallenge.Infrastructure.GitHub;
using AiAgentChallenge.Infrastructure.Orchestration;
using AiAgentChallenge.Infrastructure.Parsing;
using AiAgentChallenge.Infrastructure.Processes;
using AiAgentChallenge.Infrastructure.Projects;
using AiAgentChallenge.Infrastructure.Repositories;
using AiAgentChallenge.Infrastructure.Testing;
using AiAgentChallenge.Infrastructure.Workspace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentChallenge.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WorkspaceOptions>(configuration);
        services.Configure<RepositoryPolicyOptions>(configuration.GetSection("RepositoryPolicy"));
        services.Configure<GitOptions>(configuration.GetSection("Git"));
        services.Configure<GitHubOptions>(configuration.GetSection("GitHub"));
        services.Configure<AiOptions>(configuration.GetSection("Ai"));
        services.Configure<TestRunnerOptions>(configuration.GetSection("TestRunner"));
        services.Configure<BuildRunnerOptions>(configuration.GetSection("BuildRunner"));
        services.Configure<AsyncExecutionOptions>(configuration.GetSection("AsyncExecution"));
        services.Configure<ExecutionReportStorageOptions>(configuration.GetSection("ExecutionReports"));
        services.PostConfigure<WorkspaceOptions>(options =>
        {
            var workspaceRoot = Environment.GetEnvironmentVariable("WORKSPACE_ROOT");
            if (!string.IsNullOrWhiteSpace(workspaceRoot))
            {
                options.WorkspaceRoot = workspaceRoot;
            }

            options.WorkspaceRoot = WorkspaceService.NormalizeWorkspaceRoot(options.WorkspaceRoot);
        });
        services.PostConfigure<AiOptions>(options =>
        {
            var provider = Environment.GetEnvironmentVariable("AI_PROVIDER");
            if (!string.IsNullOrWhiteSpace(provider))
            {
                options.Provider = provider;
            }

            var model = Environment.GetEnvironmentVariable("AI_MODEL");
            if (!string.IsNullOrWhiteSpace(model))
            {
                options.Model = model;
            }
        });

        services.AddSingleton<IExecutionContextFactory, ExecutionContextFactory>();
        services.AddSingleton<INeedsFormattingRegenerationRule, NeedsFormattingRegenerationRule>();
        services.AddSingleton<INeedsAiFixRule, NeedsAiFixRule>();
        services.AddSingleton<ICanRunGitFinalizationRule, CanRunGitFinalizationRule>();
        services.AddSingleton<IExecutionStep, RepositoryPolicyValidationStep>();
        services.AddSingleton<IExecutionStep, WorkspaceCreationStep>();
        services.AddSingleton<IExecutionStep, RepositoryCloneStep>();
        services.AddSingleton<IExecutionStep, RepositoryAnalysisStep>();
        services.AddSingleton<IExecutionStep, SolutionBaselineCaptureStep>();
        services.AddSingleton<IExecutionStep, AgentContextBuildStep>();
        services.AddSingleton<IExecutionStep, AiChangePreparationStep>();
        services.AddSingleton<IExecutionStep, ApplyChangesAndSyncStep>();
        services.AddSingleton<IExecutionStep, TestAndFixWorkflowStep>();
        services.AddSingleton<IExecutionStep, GitFinalizationStep>();
        services.AddSingleton<IExecutionPipelineFactory, ExecutionPipelineFactory>();
        services.AddSingleton<ITaskSubmissionRequestValidator, TaskSubmissionRequestValidator>();
        services.AddSingleton<ITaskExecutionRequestFactory, TaskExecutionRequestFactory>();
        services.AddSingleton<IExecutionReportExporter, FileExecutionReportExporter>();
        services.AddSingleton<IExecutionReportStore, FileExecutionReportStore>();
        services.AddSingleton<IAsyncExecutionQueue, ChannelAsyncExecutionQueue>();
        services.AddSingleton<ITaskSubmissionService, TaskSubmissionService>();
        services.AddSingleton<IAsyncTaskSubmissionService, AsyncTaskSubmissionService>();
        services.AddSingleton<IAgentOrchestrator, PipelineAgentOrchestrator>();
        services.AddSingleton<ITaskParser, RuleBasedTaskParser>();
        services.AddSingleton<IRepositoryAnalyzer, RuleBasedRepositoryAnalyzer>();
        services.AddSingleton<IAgentContextBuilder, AgentContextBuilder>();
        services.AddSingleton<ISecretRedactor, RegexBasedSecretRedactor>();
        services.AddSingleton<IAiChangeValidator, AiChangeValidator>();
        services.AddSingleton<IFileChangeApplier, SafeFileChangeApplier>();
        services.AddSingleton<ITestCommandResolver, TestCommandResolver>();
        services.AddSingleton<IBuildRunner, ProcessBasedBuildRunner>();
        services.AddSingleton<ITestRunner, ProcessBasedTestRunner>();
        services.AddSingleton<IBranchNameBuilder, BranchNameBuilder>();
        services.AddSingleton<IPrDescriptionBuilder, PrDescriptionBuilder>();
        services.AddSingleton<IGitHubRepositoryParser, GitHubRepositoryParser>();
        services.AddSingleton<IWorkspaceService, WorkspaceService>();
        services.AddSingleton<IRepositoryPolicy, RepositoryPolicyValidator>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<ISolutionProjectSynchronizer, DotNetSolutionProjectSynchronizer>();
        services.AddSingleton<IGitClient, GitCliClient>();
        services.AddHttpClient<OpenAiCodeAgent>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.RequestTimeoutSeconds));
        });
        services.AddHttpClient<GeminiCodeAgent>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.RequestTimeoutSeconds));
        });
        services.AddTransient<IAiProviderCodeAgent>(serviceProvider => serviceProvider.GetRequiredService<OpenAiCodeAgent>());
        services.AddTransient<IAiProviderCodeAgent>(serviceProvider => serviceProvider.GetRequiredService<GeminiCodeAgent>());
        services.AddTransient<IAiCodeAgent, ProviderRoutingAiCodeAgent>();
        services.AddHttpClient<IPullRequestService, GitHubPullRequestService>();
        services.AddHostedService<AsyncTaskExecutionBackgroundService>();
        return services;
    }
}
