using AiAgentChallenge.Domain;
using AiAgentChallenge.Infrastructure.Analysis;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class GoFrameworkAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_ExtractsGoRoutesAndSymbols()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("internal/routes/users.go", """
            package routes

            type RegisterHandler struct {}

            func RegisterUser() {}

            func Map(router *Router) {
                router.POST("/users/register", RegisterUser)
            }
            """);

        var strategy = new GoFrameworkAnalyzer();
        var result = await strategy.AnalyzeAsync(new RepositoryScanContext
        {
            RepositoryPath = repo.RootPath,
            ParsedTask = new ParsedTask { Requirement = "Add validation to /users/register endpoint." },
            Detection = new ProjectDetection { Language = "Go" },
            Files = new[] { repo.ToAnalyzedFile("internal/routes/users.go") },
            Keywords = new[] { "users", "register" }
        });

        Assert.Contains(result.ApiEndpoints, endpoint => endpoint.HttpMethod == "POST" && endpoint.Route == "/users/register");
        Assert.Contains(result.Symbols, symbol => symbol.Name == "RegisterHandler");
        Assert.Contains(result.Symbols, symbol => symbol.Name == "RegisterUser");
    }

    private sealed class TemporaryRepository : IDisposable
    {
        public TemporaryRepository()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "go-strategy-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void AddFile(string relativePath, string content)
        {
            var fullPath = Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        public AnalyzedRepositoryFile ToAnalyzedFile(string relativePath)
        {
            var fullPath = Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return new AnalyzedRepositoryFile(fullPath, relativePath.Replace('\\', '/'), Path.GetFileName(fullPath));
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
