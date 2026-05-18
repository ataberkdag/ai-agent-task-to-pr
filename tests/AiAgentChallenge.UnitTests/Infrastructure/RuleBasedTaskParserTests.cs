using AiAgentChallenge.Infrastructure.Parsing;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class RuleBasedTaskParserTests
{
    private readonly RuleBasedTaskParser _parser = new();

    [Fact]
    public async Task ParseAsync_ParsesValidTask()
    {
        var description = """
            Repository: https://github.com/example-company/user-service
            Branch: develop

            Requirement:
            Add email validation to POST /users/register endpoint.

            Acceptance Criteria:
            - Invalid email returns HTTP 400
            - Error message should be Invalid email format
            - Add or update unit tests
            """;

        var result = await _parser.ParseAsync(description);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.ParsedTask);
        Assert.Equal("https://github.com/example-company/user-service", result.ParsedTask!.RepositoryUrl);
        Assert.Equal("develop", result.ParsedTask.BaseBranch);
        Assert.Equal("Add email validation to POST /users/register endpoint.", result.ParsedTask.Requirement);
        Assert.Equal(3, result.ParsedTask.AcceptanceCriteria.Count);
    }

    [Fact]
    public async Task ParseAsync_UsesMainWhenBranchIsMissing()
    {
        var description = """
            Repository: https://github.com/example-company/user-service

            Requirement:
            Add email validation to POST /users/register endpoint.
            """;

        var result = await _parser.ParseAsync(description);

        Assert.True(result.IsSuccess);
        Assert.Equal("main", result.ParsedTask!.BaseBranch);
    }

    [Fact]
    public async Task ParseAsync_ParsesAcceptanceCriteriaList()
    {
        var description = """
            Repository: https://github.com/example-company/user-service
            Branch: develop

            Requirement:
            Add email validation to POST /users/register endpoint.

            Acceptance Criteria:
            - Invalid email returns HTTP 400
            * Error message should be Invalid email format
            1. Add or update unit tests
            """;

        var result = await _parser.ParseAsync(description);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[]
            {
                "Invalid email returns HTTP 400",
                "Error message should be Invalid email format",
                "Add or update unit tests"
            },
            result.ParsedTask!.AcceptanceCriteria);
    }

    [Fact]
    public async Task ParseAsync_ReturnsErrorWhenRepositoryIsMissing()
    {
        var description = """
            Branch: develop

            Requirement:
            Add email validation to POST /users/register endpoint.
            """;

        var result = await _parser.ParseAsync(description);

        Assert.False(result.IsSuccess);
        Assert.Contains("Description.Repository", result.Errors.Keys);
    }

    [Fact]
    public async Task ParseAsync_ReturnsErrorWhenRequirementIsMissing()
    {
        var description = """
            Repository: https://github.com/example-company/user-service
            Branch: develop
            """;

        var result = await _parser.ParseAsync(description);

        Assert.False(result.IsSuccess);
        Assert.Contains("Description.Requirement", result.Errors.Keys);
    }

    [Fact]
    public async Task ParseAsync_ReturnsErrorWhenRepositoryUrlIsInvalid()
    {
        var description = """
            Repository: github.com/example-company/user-service
            Branch: develop

            Requirement:
            Add email validation to POST /users/register endpoint.
            """;

        var result = await _parser.ParseAsync(description);

        Assert.False(result.IsSuccess);
        Assert.Contains("Description.Repository", result.Errors.Keys);
    }
}
