using AiAgentChallenge.Domain;
using AiAgentChallenge.Infrastructure.Analysis;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class NodeJsFrameworkAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_ExtractsExpressRoutes()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("src/routes/users.js", """
            const router = require("express").Router();
            router.post("/users/register", registerUser);
            function registerUser(req, res) { }
            """);

        var strategy = new NodeJsFrameworkAnalyzer();
        var result = await strategy.AnalyzeAsync(new RepositoryScanContext
        {
            RepositoryPath = repo.RootPath,
            ParsedTask = new ParsedTask { Requirement = "Add validation to /users/register endpoint." },
            Detection = new ProjectDetection { BuildTool = "npm" },
            Files = new[] { repo.ToAnalyzedFile("src/routes/users.js") },
            Keywords = new[] { "users", "register" }
        });

        Assert.Contains(result.ApiEndpoints, endpoint => endpoint.HttpMethod == "POST" && endpoint.Route == "/users/register");
        Assert.Contains(result.Symbols, symbol => symbol.Name == "registerUser");
    }

    private sealed class TemporaryRepository : IDisposable
    {
        public TemporaryRepository()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "node-strategy-tests", Guid.NewGuid().ToString("N"));
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
