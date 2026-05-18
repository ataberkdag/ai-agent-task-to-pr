namespace AiAgentChallenge.Infrastructure.Workspace;

public sealed class WorkspaceOptions
{
    public string WorkspaceRoot { get; set; } = Path.Combine(AppContext.BaseDirectory, "workspaces");
}
