using AiAgentChallenge.Infrastructure.Git;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class BranchNameBuilderTests
{
    [Fact]
    public void Build_ReturnsGitSafeBranchName()
    {
        var builder = new BranchNameBuilder();

        var branchName = builder.Build("TASK 123/Email", "Add email validation to /users/register");

        Assert.Equal("ai-agent/TASK-123-Email-add-email-validation-to-users-register", branchName);
    }

    [Fact]
    public void Build_TruncatesLongBranchNames()
    {
        var builder = new BranchNameBuilder();

        var branchName = builder.Build(
            "TASK-123",
            new string('a', 200));

        Assert.StartsWith("ai-agent/TASK-123-", branchName);
        Assert.True(branchName.Length <= 100);
    }
}
