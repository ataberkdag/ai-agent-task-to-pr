using AiAgentChallenge.Domain;
using AiAgentChallenge.Infrastructure.Analysis;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class AspNetCoreFrameworkAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_ExtractsControllerRoutesAndSymbols()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("src/Users/RegisterController.cs", """
            namespace UserManagement.Api.Controllers;

            [ApiController]
            [Route("api/[controller]")]
            public class RegisterController : ControllerBase
            {
                private readonly IUserService _userService;

                public RegisterController(IUserService userService)
                {
                    _userService = userService;
                }

                [HttpPost("register")]
                public IActionResult Register(RegisterRequest request) => Ok();
            }
            """);

        var strategy = new AspNetCoreFrameworkAnalyzer();
        var file = repo.ToAnalyzedFile("src/Users/RegisterController.cs");
        var result = await strategy.AnalyzeAsync(new RepositoryScanContext
        {
            RepositoryPath = repo.RootPath,
            ParsedTask = new ParsedTask { Requirement = "Add validation to /users/register endpoint." },
            Detection = new ProjectDetection { Framework = "ASP.NET Core" },
            Files = new[] { file },
            Keywords = new[] { "users", "register" }
        });

        Assert.Contains(result.ApiEndpoints, endpoint => endpoint.HttpMethod == "POST" && endpoint.Route.Contains("/api/Register/register", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Symbols, symbol => symbol.Kind == "class" && symbol.Name == "RegisterController");
        Assert.Contains(result.Symbols, symbol =>
            symbol.SymbolType == "method" &&
            symbol.Name == "Register" &&
            symbol.ReturnType == "IActionResult" &&
            symbol.Parameters.Count == 1 &&
            symbol.Parameters[0].Type == "RegisterRequest" &&
            symbol.Parameters[0].Name == "request");
        Assert.Contains(result.Symbols, symbol =>
            symbol.SymbolType == "constructor" &&
            symbol.Name == "RegisterController" &&
            symbol.DisplaySignature == "RegisterController(IUserService userService)" &&
            symbol.ConstructorDependencies.Contains("IUserService"));
    }

    [Fact]
    public async Task AnalyzeAsync_ExtractsMinimalApiEndpoints()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("src/App/Program.cs", """app.MapPost("/users/register", (RegisterRequest request) => Results.Ok());""");

        var strategy = new AspNetCoreFrameworkAnalyzer();
        var result = await strategy.AnalyzeAsync(new RepositoryScanContext
        {
            RepositoryPath = repo.RootPath,
            ParsedTask = new ParsedTask { Requirement = "Add validation to /users/register endpoint." },
            Detection = new ProjectDetection { Framework = "ASP.NET Core" },
            Files = new[] { repo.ToAnalyzedFile("src/App/Program.cs") },
            Keywords = new[] { "users", "register" }
        });

        Assert.Contains(result.ApiEndpoints, endpoint => endpoint.Style == "MinimalApi" && endpoint.Route == "/users/register");
    }

    [Fact]
    public async Task AnalyzeAsync_ExtractsReferencedTypeNamesAndConstructorSignatures()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("src/Users/UserFactory.cs", """
            namespace UserManagement.Domain.Entities;

            public sealed class UserFactory
            {
                public User Create(User existingUser, CreateUserCommand command)
                {
                    return new AdminUser(
                        existingUser.Id,
                        command.FirstName,
                        command.LastName,
                        command.Email,
                        command.PermissionLevel ?? string.Empty,
                        existingUser.CreatedAtUtc);
                }
            }
            """);
        repo.AddFile("src/Users/AdminUser.cs", """
            namespace UserManagement.Domain.Entities;

            public sealed class AdminUser
            {
                public AdminUser(
                    Guid id,
                    string firstName,
                    string lastName,
                    string email,
                    string permissionLevel,
                    DateTime createdAtUtc)
                {
                }
            }
            """);

        var strategy = new AspNetCoreFrameworkAnalyzer();
        var result = await strategy.AnalyzeAsync(new RepositoryScanContext
        {
            RepositoryPath = repo.RootPath,
            ParsedTask = new ParsedTask { Requirement = "Update users flow." },
            Detection = new ProjectDetection { Framework = "ASP.NET Core" },
            Files = new[]
            {
                repo.ToAnalyzedFile("src/Users/UserFactory.cs"),
                repo.ToAnalyzedFile("src/Users/AdminUser.cs")
            },
            Keywords = new[] { "users" }
        });

        Assert.Contains(result.Symbols, symbol =>
            symbol.Name == "UserFactory" &&
            symbol.ReferencedTypeNames.Contains("AdminUser"));
        Assert.Contains(result.Symbols, symbol =>
            symbol.SymbolType == "constructor" &&
            symbol.Name == "AdminUser" &&
            symbol.DisplaySignature == "AdminUser(Guid id, string firstName, string lastName, string email, string permissionLevel, DateTime createdAtUtc)");
    }

    private sealed class TemporaryRepository : IDisposable
    {
        public TemporaryRepository()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "aspnet-strategy-tests", Guid.NewGuid().ToString("N"));
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
