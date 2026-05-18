using AiAgentChallenge.Application.Tasks;
using AiAgentChallenge.Infrastructure.Repositories;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class RepositoryPolicyValidatorTests
{
    private static RepositoryPolicyValidator CreateValidator() =>
        new(Options.Create(new RepositoryPolicyOptions
        {
            AllowedHosts = new[] { "github.com" },
            AllowedOwners = new[] { "example-company" }
        }));

    [Fact]
    public void Validate_AcceptsValidGithubHttpsUrl()
    {
        var result = CreateValidator().Validate("https://github.com/example-company/user-service");

        Assert.True(result.IsSuccess);
        Assert.Equal("example-company", result.RepositoryOwner);
    }

    [Fact]
    public void Validate_RejectsHttpUrl()
    {
        var result = CreateValidator().Validate("http://github.com/example-company/user-service");

        Assert.False(result.IsSuccess);
        Assert.Equal(RepositoryPolicyErrorCode.InvalidScheme, result.ErrorCode);
    }

    [Fact]
    public void Validate_RejectsDisallowedHost()
    {
        var result = CreateValidator().Validate("https://gitlab.com/example-company/user-service");

        Assert.False(result.IsSuccess);
        Assert.Equal(RepositoryPolicyErrorCode.DisallowedHost, result.ErrorCode);
    }

    [Fact]
    public void Validate_RejectsDisallowedOwner()
    {
        var result = CreateValidator().Validate("https://github.com/another-company/user-service");

        Assert.False(result.IsSuccess);
        Assert.Equal(RepositoryPolicyErrorCode.DisallowedOwner, result.ErrorCode);
    }

    [Fact]
    public void Validate_RejectsInvalidUrl()
    {
        var result = CreateValidator().Validate("not-a-url");

        Assert.False(result.IsSuccess);
        Assert.Equal(RepositoryPolicyErrorCode.InvalidUrl, result.ErrorCode);
    }

    [Fact]
    public void Validate_RejectsDenylistedHost()
    {
        var validator = new RepositoryPolicyValidator(Options.Create(new RepositoryPolicyOptions
        {
            AllowedHosts = new[] { "github.com" },
            AllowedOwners = new[] { "example-company" },
            DisallowedHosts = new[] { "github.com" }
        }));

        var result = validator.Validate("https://github.com/example-company/user-service");

        Assert.False(result.IsSuccess);
        Assert.Equal(RepositoryPolicyErrorCode.DisallowedHost, result.ErrorCode);
    }

    [Fact]
    public void Validate_RejectsDenylistedOwner()
    {
        var validator = new RepositoryPolicyValidator(Options.Create(new RepositoryPolicyOptions
        {
            AllowedHosts = new[] { "github.com" },
            AllowedOwners = new[] { "example-company" },
            DisallowedOwners = new[] { "example-company" }
        }));

        var result = validator.Validate("https://github.com/example-company/user-service");

        Assert.False(result.IsSuccess);
        Assert.Equal(RepositoryPolicyErrorCode.DisallowedOwner, result.ErrorCode);
    }

    [Fact]
    public void Validate_DenylistTakesPrecedenceOverAllowlist()
    {
        var validator = new RepositoryPolicyValidator(Options.Create(new RepositoryPolicyOptions
        {
            AllowedHosts = new[] { "github.com" },
            AllowedOwners = new[] { "example-company" },
            DisallowedHosts = new[] { "github.com" },
            DisallowedOwners = new[] { "example-company" }
        }));

        var result = validator.Validate("https://github.com/example-company/user-service");

        Assert.False(result.IsSuccess);
        Assert.Equal(RepositoryPolicyErrorCode.DisallowedHost, result.ErrorCode);
    }

    [Fact]
    public void Validate_AllowsValidUrl_WhenDenylistIsEmpty()
    {
        var validator = new RepositoryPolicyValidator(Options.Create(new RepositoryPolicyOptions
        {
            AllowedHosts = new[] { "github.com" },
            AllowedOwners = new[] { "example-company" },
            DisallowedHosts = Array.Empty<string>(),
            DisallowedOwners = Array.Empty<string>()
        }));

        var result = validator.Validate("https://github.com/example-company/user-service");

        Assert.True(result.IsSuccess);
    }
}
