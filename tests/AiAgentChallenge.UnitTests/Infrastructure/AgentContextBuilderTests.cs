using AiAgentChallenge.Domain;
using AiAgentChallenge.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class AgentContextBuilderTests
{
    [Fact]
    public async Task BuildAsync_IncludesRelevantFileContents()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("src/users/RegisterService.cs", "var token = \"sk-test-secret\";\npublic class RegisterService {}");

        var builder = CreateBuilder();
        var analysis = new RepositoryAnalysis
        {
            RelevantFiles = new[] { "src/users/RegisterService.cs" }
        };

        var context = await builder.BuildAsync(repo.RootPath, CreateParsedTask(), analysis);

        Assert.Single(context.SelectedFiles);
        Assert.Equal("src/users/RegisterService.cs", context.SelectedFiles[0].Path);
        Assert.Contains("RegisterService", context.SelectedFiles[0].Content);
        Assert.Contains("[REDACTED]", context.SelectedFiles[0].Content);
    }

    [Fact]
    public async Task BuildAsync_IncludesProjectFilesBeforeRelevantFiles()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("UserManagement.sln", "Project(\"{GUID}\") = \"Tests\"");
        repo.AddFile("src/users/RegisterService.cs", "public class RegisterService {}");

        var builder = CreateBuilder();
        var analysis = new RepositoryAnalysis
        {
            TestFramework = "xUnit",
            ProjectFiles = new[] { "UserManagement.sln" },
            RelevantFiles = new[] { "src/users/RegisterService.cs" }
        };

        var context = await builder.BuildAsync(repo.RootPath, CreateParsedTask(), analysis);

        Assert.Equal(2, context.SelectedFiles.Count);
        Assert.Equal("UserManagement.sln", context.SelectedFiles[0].Path);
        Assert.Equal("src/users/RegisterService.cs", context.SelectedFiles[1].Path);
        Assert.Equal("xUnit", context.TestFramework);
        Assert.Contains("Detected Test Framework: xUnit", context.RepositoryAnalysisSummary);
    }

    [Fact]
    public async Task BuildAsync_IncludesWorkspaceManifestFilesBeforeRelevantFiles()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("go.work", "go 1.22");
        repo.AddFile("go.mod", "module example.com/app");
        repo.AddFile("internal/routes/users.go", "package routes");

        var builder = CreateBuilder();
        var analysis = new RepositoryAnalysis
        {
            ProjectFiles = new[] { "go.work", "go.mod" },
            RelevantFiles = new[] { "internal/routes/users.go" }
        };

        var context = await builder.BuildAsync(repo.RootPath, CreateParsedTask(), analysis);

        Assert.Equal(3, context.SelectedFiles.Count);
        Assert.Equal("go.work", context.SelectedFiles[0].Path);
        Assert.Equal("go.mod", context.SelectedFiles[1].Path);
        Assert.Equal("internal/routes/users.go", context.SelectedFiles[2].Path);
    }

    [Fact]
    public async Task BuildAsync_DoesNotIncludeSensitiveFiles()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile(".env.production", "OPENAI_API_KEY=sk-123456789");

        var builder = CreateBuilder();
        var analysis = new RepositoryAnalysis
        {
            RelevantFiles = new[] { ".env.production" }
        };

        var context = await builder.BuildAsync(repo.RootPath, CreateParsedTask(), analysis);

        Assert.Empty(context.SelectedFiles);
    }

    [Fact]
    public async Task BuildAsync_IncludesSolutionFilesOutsideOptionalContextLimit()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("App.sln", "Microsoft Visual Studio Solution File, Format Version 12.00");
        repo.AddFile("src/App/FeatureA.cs", "public class FeatureA {}");
        repo.AddFile("src/App/FeatureB.cs", "public class FeatureB {}");

        var builder = CreateBuilder(maxContextFiles: 1);
        var analysis = new RepositoryAnalysis
        {
            ProjectFiles = new[] { "App.sln" },
            RelevantFiles = new[] { "src/App/FeatureA.cs", "src/App/FeatureB.cs" }
        };

        var context = await builder.BuildAsync(repo.RootPath, CreateParsedTask(), analysis);

        Assert.Equal(2, context.SelectedFiles.Count);
        Assert.Equal("App.sln", context.SelectedFiles[0].Path);
        Assert.Equal("src/App/FeatureA.cs", context.SelectedFiles[1].Path);
    }

    [Fact]
    public async Task BuildAsync_IncludesAllSolutionFilesBeforeOptionalFiles()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("App.sln", "Microsoft Visual Studio Solution File, Format Version 12.00");
        repo.AddFile("App.slnx", "<Solution />");
        repo.AddFile("src/App/FeatureA.cs", "public class FeatureA {}");

        var builder = CreateBuilder(maxContextFiles: 1);
        var analysis = new RepositoryAnalysis
        {
            ProjectFiles = new[] { "App.sln", "App.slnx" },
            RelevantFiles = new[] { "src/App/FeatureA.cs" }
        };

        var context = await builder.BuildAsync(repo.RootPath, CreateParsedTask(), analysis);

        Assert.Equal(3, context.SelectedFiles.Count);
        Assert.Equal("App.sln", context.SelectedFiles[0].Path);
        Assert.Equal("App.slnx", context.SelectedFiles[1].Path);
        Assert.Equal("src/App/FeatureA.cs", context.SelectedFiles[2].Path);
    }

    [Fact]
    public async Task BuildAsync_IncludesLargeSolutionFiles()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("App.sln", new string('S', 5000));

        var builder = CreateBuilder(maxFileBytes: 64);
        var analysis = new RepositoryAnalysis
        {
            ProjectFiles = new[] { "App.sln" }
        };

        var context = await builder.BuildAsync(repo.RootPath, CreateParsedTask(), analysis);

        Assert.Single(context.SelectedFiles);
        Assert.Equal("App.sln", context.SelectedFiles[0].Path);
        Assert.Equal(5000, context.SelectedFiles[0].Content.Length);
    }

    [Fact]
    public async Task BuildAsync_StillExcludesLargeNonSolutionFiles()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("src/App/FeatureA.cs", new string('C', 5000));

        var builder = CreateBuilder(maxFileBytes: 64);
        var analysis = new RepositoryAnalysis
        {
            RelevantFiles = new[] { "src/App/FeatureA.cs" }
        };

        var context = await builder.BuildAsync(repo.RootPath, CreateParsedTask(), analysis);

        Assert.Empty(context.SelectedFiles);
    }

    [Fact]
    public async Task BuildAsync_IncludesCriticalSignaturesWithExactOrder()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("src/users/UserFactory.cs", "public class UserFactory {}");
        repo.AddFile("src/users/AdminUser.cs", "public class AdminUser {}");

        var builder = CreateBuilder();
        var analysis = new RepositoryAnalysis
        {
            RelevantFiles = new[] { "src/users/UserFactory.cs", "src/users/AdminUser.cs" },
            Symbols = new[]
            {
                new CodeSymbolInfo
                {
                    Kind = "class",
                    SymbolType = "class",
                    Name = "UserFactory",
                    SourceFile = "src/users/UserFactory.cs",
                    ReferencedTypeNames = new[] { "AdminUser" }
                },
                new CodeSymbolInfo
                {
                    Kind = "constructor",
                    SymbolType = "constructor",
                    Name = "AdminUser",
                    SourceFile = "src/users/AdminUser.cs",
                    DisplaySignature = "AdminUser(Guid id, string firstName, string lastName, string email, string permissionLevel, DateTime createdAtUtc)",
                    ReferencedTypeNames = Array.Empty<string>()
                }
            }
        };

        var context = await builder.BuildAsync(repo.RootPath, CreateParsedTask(), analysis);

        Assert.Contains("Critical Signatures:", context.RepositoryAnalysisSummary);
        Assert.Contains("AdminUser(Guid id, string firstName, string lastName, string email, string permissionLevel, DateTime createdAtUtc)", context.RepositoryAnalysisSummary);
    }

    [Fact]
    public async Task BuildAsync_UsesConfiguredMaxCriticalSignaturesAndIncludesDotNetMetadata()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("src/users/UserFactory.cs", "public class UserFactory {}");

        var builder = CreateBuilder(maxCriticalSignatures: 1);
        var analysis = new RepositoryAnalysis
        {
            RelevantFiles = new[] { "src/users/UserFactory.cs" },
            AvailableTestLibraries = new[] { "xUnit", "Moq", "FluentAssertions" },
            TargetFramework = "net8.0",
            TargetFrameworks = new[] { "net8.0", "net9.0" },
            LangVersion = "12.0",
            Symbols = new[]
            {
                new CodeSymbolInfo
                {
                    Kind = "constructor",
                    SymbolType = "constructor",
                    Name = "AdminUser",
                    SourceFile = "src/users/UserFactory.cs",
                    DisplaySignature = "AdminUser(Guid id)"
                },
                new CodeSymbolInfo
                {
                    Kind = "constructor",
                    SymbolType = "constructor",
                    Name = "CustomerUser",
                    SourceFile = "src/users/UserFactory.cs",
                    DisplaySignature = "CustomerUser(Guid id)"
                }
            }
        };

        var context = await builder.BuildAsync(repo.RootPath, CreateParsedTask(), analysis);

        Assert.Contains("Available Test Libraries: xUnit, Moq, FluentAssertions", context.RepositoryAnalysisSummary);
        Assert.Contains("Target Framework: net8.0", context.RepositoryAnalysisSummary);
        Assert.Contains("Target Frameworks: net8.0, net9.0", context.RepositoryAnalysisSummary);
        Assert.Contains("LangVersion: 12.0", context.RepositoryAnalysisSummary);
        Assert.Contains("AdminUser(Guid id)", context.RepositoryAnalysisSummary);
        Assert.DoesNotContain("CustomerUser(Guid id)", context.RepositoryAnalysisSummary);
    }

    [Fact]
    public async Task BuildAsync_DeduplicatesEquivalentCriticalSignatures_BySymbolIdentity()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("src/users/UserFactory.cs", "public class UserFactory {}");

        var builder = CreateBuilder(maxCriticalSignatures: 10);
        var analysis = new RepositoryAnalysis
        {
            RelevantFiles = new[] { "src/users/UserFactory.cs" },
            Symbols = new[]
            {
                new CodeSymbolInfo
                {
                    Kind = "constructor",
                    SymbolType = "constructor",
                    Name = "UserValidationException",
                    SourceFile = "src/users/UserFactory.cs",
                    DisplaySignature = "UserValidationException(string message)",
                    Parameters = new[]
                    {
                        new CodeParameterInfo
                        {
                            Name = "message",
                            Type = "string",
                            Ordinal = 0
                        }
                    }
                },
                new CodeSymbolInfo
                {
                    Kind = "constructor",
                    SymbolType = "constructor",
                    Name = "UserValidationException",
                    SourceFile = "src/users/UserFactory.cs",
                    DisplaySignature = """
                        UserValidationException(string message)
                            : base()
                        """,
                    Parameters = new[]
                    {
                        new CodeParameterInfo
                        {
                            Name = "message",
                            Type = "string",
                            Ordinal = 0
                        }
                    }
                },
                new CodeSymbolInfo
                {
                    Kind = "constructor",
                    SymbolType = "constructor",
                    Name = "AdminUser",
                    SourceFile = "src/users/UserFactory.cs",
                    DisplaySignature = """
                        AdminUser(
                            Guid id,
                            string firstName,
                            DateTime createdAtUtc)
                            : base()
                        """,
                    Parameters = new[]
                    {
                        new CodeParameterInfo
                        {
                            Name = "id",
                            Type = "Guid",
                            Ordinal = 0
                        },
                        new CodeParameterInfo
                        {
                            Name = "firstName",
                            Type = "string",
                            Ordinal = 1
                        },
                        new CodeParameterInfo
                        {
                            Name = "createdAtUtc",
                            Type = "DateTime",
                            Ordinal = 2
                        }
                    }
                },
                new CodeSymbolInfo
                {
                    Kind = "constructor",
                    SymbolType = "constructor",
                    Name = "AdminUser",
                    SourceFile = "src/users/UserFactory.cs",
                    DisplaySignature = "AdminUser(Guid id, string firstName, DateTime createdAtUtc)",
                    Parameters = new[]
                    {
                        new CodeParameterInfo
                        {
                            Name = "id",
                            Type = "Guid",
                            Ordinal = 0
                        },
                        new CodeParameterInfo
                        {
                            Name = "firstName",
                            Type = "string",
                            Ordinal = 1
                        },
                        new CodeParameterInfo
                        {
                            Name = "createdAtUtc",
                            Type = "DateTime",
                            Ordinal = 2
                        }
                    }
                },
                new CodeSymbolInfo
                {
                    Kind = "record",
                    SymbolType = "record",
                    Name = "CreateUserCommand",
                    SourceFile = "src/users/UserFactory.cs",
                    DisplaySignature = "sealed record CreateUserCommand(string FirstName, string LastName)"
                }
            }
        };

        var context = await builder.BuildAsync(repo.RootPath, CreateParsedTask(), analysis);

        Assert.Equal(1, CountOccurrences(context.RepositoryAnalysisSummary, "UserValidationException(string message)"));
        Assert.Equal(1, CountOccurrences(context.RepositoryAnalysisSummary, "AdminUser(Guid id, string firstName, DateTime createdAtUtc)"));
        Assert.DoesNotContain(": base()", context.RepositoryAnalysisSummary);
        Assert.DoesNotContain("AdminUser( Guid id", context.RepositoryAnalysisSummary);
        Assert.DoesNotContain("UserValidationException( string message )", context.RepositoryAnalysisSummary);
        Assert.Contains("CreateUserCommand(string FirstName, string LastName)", context.RepositoryAnalysisSummary);
        Assert.DoesNotContain("sealed record CreateUserCommand", context.RepositoryAnalysisSummary);
    }

    [Fact]
    public async Task BuildAsync_DeduplicatesEquivalentCriticalSignatures_ByFinalDisplay()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("src/users/UserFactory.cs", "public class UserFactory {}");

        var builder = CreateBuilder(maxCriticalSignatures: 10);
        var analysis = new RepositoryAnalysis
        {
            RelevantFiles = new[] { "src/users/UserFactory.cs" },
            Symbols = new[]
            {
                new CodeSymbolInfo
                {
                    Kind = "constructor",
                    SymbolType = "constructor",
                    Name = "AdminUser",
                    SourceFile = "src/users/UserFactory.cs",
                    DisplaySignature = "AdminUser(Guid id, string firstName, DateTime createdAtUtc)",
                    Parameters = new[]
                    {
                        new CodeParameterInfo
                        {
                            Name = "id",
                            Type = "Guid",
                            Ordinal = 0
                        },
                        new CodeParameterInfo
                        {
                            Name = "firstName",
                            Type = "string",
                            Ordinal = 1
                        },
                        new CodeParameterInfo
                        {
                            Name = "createdAtUtc",
                            Type = "DateTime",
                            Ordinal = 2
                        }
                    }
                },
                new CodeSymbolInfo
                {
                    Kind = "constructor",
                    SymbolType = "constructor",
                    Name = "AdminUser",
                    SourceFile = "src/users/UserFactory.cs",
                    DisplaySignature = """
                        AdminUser(
                            Guid id,
                            string firstName,
                            DateTime createdAtUtc)
                        """
                }
            }
        };

        var context = await builder.BuildAsync(repo.RootPath, CreateParsedTask(), analysis);

        Assert.Equal(1, CountOccurrences(context.RepositoryAnalysisSummary, "AdminUser(Guid id, string firstName, DateTime createdAtUtc)"));
    }

    private static AgentContextBuilder CreateBuilder(int maxContextFiles = 10, int maxFileBytes = 4096, int maxCriticalSignatures = 50)
    {
        return new AgentContextBuilder(
            Options.Create(new AiOptions
            {
                MaxContextFiles = maxContextFiles,
                MaxFileBytes = maxFileBytes,
                MaxCriticalSignatures = maxCriticalSignatures
            }),
            new RegexBasedSecretRedactor());
    }

    private static ParsedTask CreateParsedTask()
    {
        return new ParsedTask
        {
            RepositoryUrl = "https://github.com/example-company/user-service",
            BaseBranch = "main",
            Requirement = "Add validation",
            AcceptanceCriteria = Array.Empty<string>()
        };
    }

    private static int CountOccurrences(string value, string expected)
    {
        var count = 0;
        var startIndex = 0;

        while (true)
        {
            var index = value.IndexOf(expected, startIndex, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            count++;
            startIndex = index + expected.Length;
        }
    }

    private sealed class TemporaryRepository : IDisposable
    {
        public TemporaryRepository()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "agent-context-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void AddFile(string relativePath, string content)
        {
            var fullPath = Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
