using AiAgentChallenge.Infrastructure.Testing;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class TestCommandResolverTests
{
    [Fact]
    public void Resolve_DotnetTest_IsSupported()
    {
        var result = new TestCommandResolver().Resolve(@"C:\repo", "dotnet test");

        Assert.True(result.IsSupported);
        Assert.Equal("dotnet", result.Executable);
        Assert.Equal(new[] { "test" }, result.Arguments);
    }

    [Fact]
    public void Resolve_MvnTest_IsSupported()
    {
        var result = new TestCommandResolver().Resolve(@"C:\repo", "mvn test");

        Assert.True(result.IsSupported);
        Assert.Equal("mvn", result.Executable);
    }

    [Fact]
    public void Resolve_NpmTest_IsSupported()
    {
        var result = new TestCommandResolver().Resolve(@"C:\repo", "npm test");

        Assert.True(result.IsSupported);
        Assert.Equal("npm", result.Executable);
    }

    [Fact]
    public void Resolve_Pytest_IsSupported()
    {
        var result = new TestCommandResolver().Resolve(@"C:\repo", "pytest");

        Assert.True(result.IsSupported);
        Assert.Equal("pytest", result.Executable);
        Assert.Empty(result.Arguments);
    }

    [Fact]
    public void Resolve_UnsupportedCommand_IsRejected()
    {
        var result = new TestCommandResolver().Resolve(@"C:\repo", "dotnet test --filter Unit");

        Assert.False(result.IsSupported);
    }
}
