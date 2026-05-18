using AiAgentChallenge.Infrastructure.Git;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class GitHubRepositoryParserTests
{
    [Fact]
    public void Parse_ExtractsOwnerAndRepositoryName()
    {
        var parser = new GitHubRepositoryParser();

        var result = parser.Parse("https://github.com/example-company/user-service");

        Assert.Equal("example-company", result.Owner);
        Assert.Equal("user-service", result.RepositoryName);
    }

    [Fact]
    public void Parse_StripsGitSuffix()
    {
        var parser = new GitHubRepositoryParser();

        var result = parser.Parse("https://github.com/example-company/user-service.git");

        Assert.Equal("example-company", result.Owner);
        Assert.Equal("user-service", result.RepositoryName);
    }
}
