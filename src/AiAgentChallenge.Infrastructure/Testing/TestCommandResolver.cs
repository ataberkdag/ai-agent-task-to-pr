using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Application.Tasks;

namespace AiAgentChallenge.Infrastructure.Testing;

public sealed class TestCommandResolver : ITestCommandResolver
{
    public TestCommandResolution Resolve(string repositoryPath, string testCommand)
    {
        var normalized = Normalize(testCommand);

        return normalized switch
        {
            "dotnet test" => TestCommandResolution.Supported(normalized, "dotnet", new[] { "test" }),
            "mvn test" => TestCommandResolution.Supported(normalized, "mvn", new[] { "test" }),
            "npm test" => TestCommandResolution.Supported(normalized, "npm", new[] { "test" }),
            "pytest" => TestCommandResolution.Supported(normalized, "pytest", Array.Empty<string>()),
            "./gradlew test" => ResolveGradleWrapper(repositoryPath, normalized),
            "gradle test" => TestCommandResolution.Supported(normalized, "gradle", new[] { "test" }),
            "" => TestCommandResolution.Unsupported(normalized, "Test command is empty."),
            _ => TestCommandResolution.Unsupported(normalized, $"Test command '{testCommand}' is not supported.")
        };
    }

    private static TestCommandResolution ResolveGradleWrapper(string repositoryPath, string normalized)
    {
        var gradlewBat = Path.Combine(repositoryPath, "gradlew.bat");
        var gradlew = Path.Combine(repositoryPath, "gradlew");

        if (OperatingSystem.IsWindows() && File.Exists(gradlewBat))
        {
            return TestCommandResolution.Supported(normalized, gradlewBat, new[] { "test" });
        }

        if (File.Exists(gradlew))
        {
            return TestCommandResolution.Supported(normalized, gradlew, new[] { "test" });
        }

        return TestCommandResolution.Unsupported(normalized, "Gradle wrapper script was not found in the repository root.");
    }

    private static string Normalize(string testCommand)
    {
        return string.Join(' ',
            (testCommand ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Trim()
            .ToLowerInvariant();
    }
}
