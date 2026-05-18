using AiAgentChallenge.Domain;
using AiAgentChallenge.Infrastructure.Analysis;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class RuleBasedRepositoryAnalyzerTests
{
    private readonly RuleBasedRepositoryAnalyzer _analyzer = new();

    [Fact]
    public async Task AnalyzeAsync_DetectsDotNetRepository()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("UserManagement.sln", """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{GUID}") = "UserManagement.Api", "src/App/App.csproj", "{GUID2}"
            Project("{GUID}") = "UserManagement.Tests", "tests/App.Tests/App.Tests.csproj", "{GUID3}"
            EndProject
            """);
        repo.AddFile("src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        repo.AddFile("tests/App.Tests/App.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="xunit" Version="2.5.3" />
                <Using Include="Xunit" />
              </ItemGroup>
            </Project>
            """);
        repo.AddFile("src/Users/RegisterController.cs", """
            namespace UserManagement.Api.Controllers;

            [ApiController]
            [Route("api/[controller]")]
            public class RegisterController : ControllerBase
            {
                [HttpPost("register")]
                public IActionResult Register(RegisterRequest request) => Ok();
            }
            """);

        var analysis = await _analyzer.AnalyzeAsync(repo.RootPath, CreateParsedTask("Add validation to /users/register endpoint."));

        Assert.Equal("C#", analysis.Language);
        Assert.Equal("ASP.NET Core", analysis.Framework);
        Assert.Equal("dotnet", analysis.BuildTool);
        Assert.Equal("dotnet test", analysis.TestCommand);
        Assert.Equal("xUnit", analysis.TestFramework);
        Assert.Contains("xUnit", analysis.AvailableTestLibraries);
        Assert.Equal("net8.0", analysis.TargetFramework);
        Assert.Contains("UserManagement.sln", analysis.ProjectFiles);
        Assert.Contains("src/App/App.csproj", analysis.ProjectFiles);
        Assert.Contains(analysis.ApiEndpoints, endpoint => endpoint.HttpMethod == "POST" && endpoint.Route.Contains("/api/Register/register", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.Symbols, symbol => symbol.Kind == "class" && symbol.Name == "RegisterController");
    }

    [Fact]
    public async Task AnalyzeAsync_ReadsLangVersionAndMultiTargetFrameworks()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("App.sln", "Microsoft Visual Studio Solution File, Format Version 12.00");
        repo.AddFile("Directory.Build.props", """
            <Project>
              <PropertyGroup>
                <LangVersion>12.0</LangVersion>
              </PropertyGroup>
            </Project>
            """);
        repo.AddFile("src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFrameworks>net8.0;net9.0</TargetFrameworks>
              </PropertyGroup>
            </Project>
            """);

        var analysis = await _analyzer.AnalyzeAsync(repo.RootPath, CreateParsedTask("Add validation."));

        Assert.Equal("net8.0", analysis.TargetFramework);
        Assert.Equal(new[] { "net8.0", "net9.0" }, analysis.TargetFrameworks);
        Assert.Equal("12.0", analysis.LangVersion);
    }

    [Fact]
    public async Task AnalyzeAsync_IncludesSlnxBeforeCsprojForDotNetRepositories()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("UserManagement.slnx", "<Solution />");
        repo.AddFile("src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var analysis = await _analyzer.AnalyzeAsync(repo.RootPath, CreateParsedTask("Add validation."));

        Assert.Equal("C#", analysis.Language);
        Assert.Contains("UserManagement.slnx", analysis.ProjectFiles);
        Assert.Equal("UserManagement.slnx", analysis.ProjectFiles[0]);
        Assert.Equal("src/App/App.csproj", analysis.ProjectFiles[1]);
    }

    [Fact]
    public async Task AnalyzeAsync_DetectsMavenRepository()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("pom.xml", "<project><artifactId>demo</artifactId></project>");

        var analysis = await _analyzer.AnalyzeAsync(repo.RootPath, CreateParsedTask("Add validation."));

        Assert.Equal("Java", analysis.Language);
        Assert.Equal("Java", analysis.Framework);
        Assert.Equal("Maven", analysis.BuildTool);
        Assert.Equal("mvn test", analysis.TestCommand);
    }

    [Fact]
    public async Task AnalyzeAsync_DetectsNUnitTestFrameworkForDotNetRepositories()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("App.sln", "Microsoft Visual Studio Solution File, Format Version 12.00");
        repo.AddFile("tests/App.Tests/App.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="NUnit" Version="4.0.1" />
              </ItemGroup>
            </Project>
            """);

        var analysis = await _analyzer.AnalyzeAsync(repo.RootPath, CreateParsedTask("Add validation."));

        Assert.Equal("NUnit", analysis.TestFramework);
    }

    [Fact]
    public async Task AnalyzeAsync_DetectsMSTestTestFrameworkForDotNetRepositories()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("App.sln", "Microsoft Visual Studio Solution File, Format Version 12.00");
        repo.AddFile("tests/App.Tests/App.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="MSTest.TestFramework" Version="3.0.0" />
              </ItemGroup>
            </Project>
            """);

        var analysis = await _analyzer.AnalyzeAsync(repo.RootPath, CreateParsedTask("Add validation."));

        Assert.Equal("MSTest", analysis.TestFramework);
    }

    [Fact]
    public async Task AnalyzeAsync_DetectsAvailableTestLibrariesFromCsproj()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("App.sln", "Microsoft Visual Studio Solution File, Format Version 12.00");
        repo.AddFile("tests/App.Tests/App.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="xunit" Version="2.5.3" />
                <PackageReference Include="Moq" Version="4.20.0" />
                <PackageReference Include="FluentAssertions" Version="6.12.0" />
              </ItemGroup>
            </Project>
            """);

        var analysis = await _analyzer.AnalyzeAsync(repo.RootPath, CreateParsedTask("Add validation."));

        Assert.Equal(new[] { "FluentAssertions", "Moq", "xUnit" }, analysis.AvailableTestLibraries);
    }

    [Fact]
    public async Task AnalyzeAsync_DetectsNodeRepository()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("package.json", """{ "name": "app" }""");
        repo.AddFile("tsconfig.json", """{ "compilerOptions": {} }""");
        repo.AddFile("pnpm-workspace.yaml", "packages:\n  - apps/*");
        repo.AddFile("src/routes/users.js", """router.post('/users/register', registerUser);""");

        var analysis = await _analyzer.AnalyzeAsync(repo.RootPath, CreateParsedTask("Add validation."));

        Assert.Equal("JavaScript/TypeScript", analysis.Language);
        Assert.Equal("JavaScript/TypeScript", analysis.Framework);
        Assert.Equal("npm", analysis.BuildTool);
        Assert.Equal("npm test", analysis.TestCommand);
        Assert.Equal("pnpm-workspace.yaml", analysis.ProjectFiles[0]);
        Assert.Equal("tsconfig.json", analysis.ProjectFiles[1]);
        Assert.Contains("package.json", analysis.ProjectFiles);
        Assert.Contains(analysis.ApiEndpoints, endpoint => endpoint.HttpMethod == "POST" && endpoint.Route == "/users/register");
    }

    [Fact]
    public async Task AnalyzeAsync_DetectsGoRepository()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("go.work", "go 1.22");
        repo.AddFile("go.mod", "module example.com/app");
        repo.AddFile("internal/routes/users.go", """
            package routes

            func Map(router *Router) {
                router.POST("/users/register", RegisterUser)
            }
            """);

        var analysis = await _analyzer.AnalyzeAsync(repo.RootPath, CreateParsedTask("Add validation to /users/register endpoint."));

        Assert.Equal("Go", analysis.Language);
        Assert.Equal("Go", analysis.Framework);
        Assert.Equal("go", analysis.BuildTool);
        Assert.Equal("go test ./...", analysis.TestCommand);
        Assert.Equal("go.work", analysis.ProjectFiles[0]);
        Assert.Contains("go.mod", analysis.ProjectFiles);
        Assert.Contains(analysis.ApiEndpoints, endpoint => endpoint.HttpMethod == "POST" && endpoint.Route == "/users/register");
    }

    [Fact]
    public async Task AnalyzeAsync_IncludesGradleSettingsBeforeBuildFile()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("settings.gradle.kts", "rootProject.name = \"demo\"");
        repo.AddFile("build.gradle.kts", """
            plugins {
                kotlin("jvm") version "1.9.0"
            }
            """);

        var analysis = await _analyzer.AnalyzeAsync(repo.RootPath, CreateParsedTask("Add validation."));

        Assert.Equal("Java/Kotlin", analysis.Language);
        Assert.Equal("settings.gradle.kts", analysis.ProjectFiles[0]);
        Assert.Contains("build.gradle.kts", analysis.ProjectFiles);
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnsUnknownForUnrecognizedRepository()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("README.md", "# repo");

        var analysis = await _analyzer.AnalyzeAsync(repo.RootPath, CreateParsedTask("Add validation."));

        Assert.Equal("Unknown", analysis.Language);
        Assert.Equal("Unknown", analysis.Framework);
        Assert.Equal("Unknown", analysis.BuildTool);
        Assert.Equal(string.Empty, analysis.TestCommand);
    }

    [Fact]
    public async Task AnalyzeAsync_IgnoresConfiguredDirectories()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("src/users/RegisterService.cs", "public class RegisterService {}");
        repo.AddFile("node_modules/users/FakeMatch.js", "register users");
        repo.AddFile("bin/users/FakeMatch.cs", "register users");

        var analysis = await _analyzer.AnalyzeAsync(repo.RootPath, CreateParsedTask("Update users register flow."));

        Assert.Contains("src/users/RegisterService.cs", analysis.RelevantFiles);
        Assert.DoesNotContain(analysis.RelevantFiles, path => path.Contains("node_modules", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(analysis.RelevantFiles, path => path.Contains("/bin/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnalyzeAsync_FindsRelevantFilesFromRequirementKeywords()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("src/users/RegisterService.cs", "public class RegisterService {}");
        repo.AddFile("src/orders/OrderService.cs", "public class OrderService {}");

        var analysis = await _analyzer.AnalyzeAsync(repo.RootPath, CreateParsedTask("Add email validation to /users/register endpoint."));

        Assert.Contains("src/users/RegisterService.cs", analysis.RelevantFiles);
        Assert.DoesNotContain("src/orders/OrderService.cs", analysis.RelevantFiles);
    }

    [Fact]
    public async Task AnalyzeAsync_ExtractsKeywordsFromEndpointPath()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("src/users/RegisterController.cs", "public class RegisterController {}");

        var analysis = await _analyzer.AnalyzeAsync(repo.RootPath, CreateParsedTask("Add email validation to POST /users/register endpoint."));

        Assert.Contains("src/users/RegisterController.cs", analysis.RelevantFiles);
    }

    [Fact]
    public async Task AnalyzeAsync_DetectsMinimalApiEndpoints()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        repo.AddFile("src/App/Program.cs", """
            var app = builder.Build();
            app.MapPost("/users/register", (RegisterRequest request) => Results.Ok());
            """);

        var analysis = await _analyzer.AnalyzeAsync(repo.RootPath, CreateParsedTask("Add validation to /users/register endpoint."));

        Assert.Contains(analysis.ApiEndpoints, endpoint => endpoint.Style == "MinimalApi" && endpoint.Route == "/users/register");
        Assert.Contains("src/App/Program.cs", analysis.RelevantFiles);
    }

    [Fact]
    public async Task AnalyzeAsync_FindsExistingTestFiles()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("tests/users/RegisterServiceTests.cs", "public class RegisterServiceTests {}");
        repo.AddFile("src/users/RegisterService.cs", "public class RegisterService {}");

        var analysis = await _analyzer.AnalyzeAsync(repo.RootPath, CreateParsedTask("Update /users/register endpoint."));

        Assert.Contains("tests/users/RegisterServiceTests.cs", analysis.ExistingTestFiles);
    }

    [Fact]
    public async Task AnalyzeAsync_ExpandsRelevantFilesWithReferencedTypeDependencies()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("App.sln", "Microsoft Visual Studio Solution File, Format Version 12.00");
        repo.AddFile("src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
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

        var analysis = await _analyzer.AnalyzeAsync(repo.RootPath, CreateParsedTask("Update users flow."));

        Assert.Contains("src/Users/UserFactory.cs", analysis.RelevantFiles);
        Assert.Contains("src/Users/AdminUser.cs", analysis.RelevantFiles);
    }

    [Fact]
    public async Task AnalyzeAsync_DependencyWalk_IncludesServiceFactoryFamilyAndBaseType()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("App.sln", "Microsoft Visual Studio Solution File, Format Version 12.00");
        repo.AddFile("src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        repo.AddFile("src/App/DependencyInjection.cs", """
            public static class DependencyInjection
            {
                public static IServiceCollection AddInfrastructure(this IServiceCollection services)
                {
                    services.AddScoped<IUserService, UserService>();
                    services.AddScoped<IUserFactory, UserFactory>();
                    services.AddScoped<IUserRepository, UserRepository>();
                    return services;
                }
            }
            """);
        repo.AddFile("src/Users/UsersController.cs", """
            [ApiController]
            [Route("api/[controller]")]
            public sealed class UsersController : ControllerBase
            {
                [HttpPut]
                public Task<IActionResult> Put([FromBody] UpdateUserRequest request, [FromServices] IUserService userService, CancellationToken cancellationToken)
                    => Task.FromResult<IActionResult>(Ok());
            }
            """);
        repo.AddFile("src/Users/IUserService.cs", """
            public interface IUserService
            {
                Task<UserResponse> UpdateAsync(UpdateUserCommand command, CancellationToken cancellationToken);
            }
            """);
        repo.AddFile("src/Users/UserService.cs", """
            public sealed class UserService : IUserService
            {
                private readonly IUserFactory _userFactory;
                private readonly IUserRepository _userRepository;
                private readonly IUserHelper _userHelper;

                public UserService(IUserFactory userFactory, IUserRepository userRepository, IUserHelper userHelper)
                {
                    _userFactory = userFactory;
                    _userRepository = userRepository;
                    _userHelper = userHelper;
                }

                public Task<UserResponse> UpdateAsync(UpdateUserCommand command, CancellationToken cancellationToken)
                {
                    var existingUser = _userRepository.GetByIdAsync(command.Id, cancellationToken);
                    var updatedUser = _userFactory.Create(existingUser.Result!, command);
                    return Task.FromResult(new UserResponse(updatedUser.Id));
                }
            }
            """);
        repo.AddFile("src/Users/IUserFactory.cs", """
            public interface IUserFactory
            {
                User Create(User existingUser, UpdateUserCommand command);
            }
            """);
        repo.AddFile("src/Users/UserFactory.cs", """
            public sealed class UserFactory : IUserFactory
            {
                public User Create(User existingUser, UpdateUserCommand command)
                {
                    return command.UserType switch
                    {
                        UserType.Admin => new AdminUser(command.Id, command.FirstName, command.LastName, command.Email, command.PermissionLevel!, existingUser.CreatedAtUtc),
                        UserType.Customer => new CustomerUser(command.Id, command.FirstName, command.LastName, command.Email, command.LoyaltyCode!, existingUser.CreatedAtUtc),
                        _ => new EmployeeUser(command.Id, command.FirstName, command.LastName, command.Email, command.Department!, existingUser.CreatedAtUtc)
                    };
                }
            }
            """);
        repo.AddFile("src/Users/IUserRepository.cs", """
            public interface IUserRepository
            {
                Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
            }
            """);
        repo.AddFile("src/Users/UserRepository.cs", "public sealed class UserRepository : IUserRepository { public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<User?>(null); }");
        repo.AddFile("src/Users/IUserHelper.cs", "public interface IUserHelper { }");
        repo.AddFile("src/Users/MyTestHelper.cs", "public sealed class MyTestHelper : IUserHelper { }");
        repo.AddFile("src/Users/User.cs", """
            public abstract class User
            {
                public Guid Id { get; }
                public DateTime CreatedAtUtc { get; }
            }
            """);
        repo.AddFile("src/Users/AdminUser.cs", "public sealed class AdminUser : User { public AdminUser(Guid id, string firstName, string lastName, string email, string permissionLevel, DateTime createdAtUtc) { } }");
        repo.AddFile("src/Users/CustomerUser.cs", "public sealed class CustomerUser : User { public CustomerUser(Guid id, string firstName, string lastName, string email, string loyaltyCode, DateTime createdAtUtc) { } }");
        repo.AddFile("src/Users/EmployeeUser.cs", "public sealed class EmployeeUser : User { public EmployeeUser(Guid id, string firstName, string lastName, string email, string department, DateTime createdAtUtc) { } }");
        repo.AddFile("src/Users/UpdateUserCommand.cs", "public sealed record UpdateUserCommand(Guid Id, string FirstName, string LastName, string Email, UserType UserType, string? PermissionLevel, string? LoyaltyCode, string? Department);");
        repo.AddFile("src/Users/UpdateUserRequest.cs", "public sealed record UpdateUserRequest(Guid Id, string FirstName, string LastName, string Email, UserType UserType);");
        repo.AddFile("src/Users/UserResponse.cs", "public sealed record UserResponse(Guid Id);");
        repo.AddFile("src/Users/UserType.cs", "public enum UserType { Admin, Customer, Employee }");

        var analysis = await _analyzer.AnalyzeAsync(repo.RootPath, CreateParsedTask("Add PUT api/users endpoint and flow."));

        Assert.Contains("src/Users/UsersController.cs", analysis.RelevantFiles);
        Assert.Contains("src/Users/UserService.cs", analysis.RelevantFiles);
        Assert.Contains("src/Users/UserFactory.cs", analysis.RelevantFiles);
        Assert.Contains("src/Users/User.cs", analysis.RelevantFiles);
        Assert.Contains("src/Users/AdminUser.cs", analysis.RelevantFiles);
        Assert.Contains("src/Users/CustomerUser.cs", analysis.RelevantFiles);
        Assert.Contains("src/Users/EmployeeUser.cs", analysis.RelevantFiles);
        Assert.Contains("src/Users/UpdateUserCommand.cs", analysis.RelevantFiles);
        Assert.Contains("src/Users/UserResponse.cs", analysis.RelevantFiles);
    }

    [Fact]
    public async Task AnalyzeAsync_DependencyWalk_ResolvesMismatchedImplementationNameFromDiRegistration()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("App.sln", "Microsoft Visual Studio Solution File, Format Version 12.00");
        repo.AddFile("src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        repo.AddFile("src/App/DependencyInjection.cs", """
            public static class DependencyInjection
            {
                public static IServiceCollection AddInfrastructure(this IServiceCollection services)
                {
                    services.AddScoped<IUserHelper, MyTestHelper>();
                    services.AddScoped<IUserService, UserService>();
                    return services;
                }
            }
            """);
        repo.AddFile("src/Users/UsersController.cs", """
            [ApiController]
            [Route("api/[controller]")]
            public sealed class UsersController : ControllerBase
            {
                [HttpPut]
                public IActionResult Put([FromServices] IUserService userService) => Ok();
            }
            """);
        repo.AddFile("src/Users/IUserService.cs", "public interface IUserService { }");
        repo.AddFile("src/Users/UserService.cs", "public sealed class UserService : IUserService { public UserService(IUserHelper userHelper) { } }");
        repo.AddFile("src/Users/IUserHelper.cs", "public interface IUserHelper { }");
        repo.AddFile("src/Users/MyTestHelper.cs", "public sealed class MyTestHelper : IUserHelper { }");

        var analysis = await _analyzer.AnalyzeAsync(repo.RootPath, CreateParsedTask("Add PUT api/users endpoint and flow."));

        Assert.Contains("src/Users/MyTestHelper.cs", analysis.RelevantFiles);
    }

    [Fact]
    public async Task AnalyzeAsync_DoesNotIncludeSensitiveFilesInRelevantFiles()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("src/users/RegisterService.cs", "public class RegisterService {}");
        repo.AddFile(".env.production", "users register secret");
        repo.AddFile("secrets.json", """{ "users": "register" }""");

        var analysis = await _analyzer.AnalyzeAsync(repo.RootPath, CreateParsedTask("Update /users/register endpoint."));

        Assert.Contains("src/users/RegisterService.cs", analysis.RelevantFiles);
        Assert.DoesNotContain(analysis.RelevantFiles, path => path.Contains(".env", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(analysis.RelevantFiles, path => path.Contains("secrets.json", StringComparison.OrdinalIgnoreCase));
    }

    private static ParsedTask CreateParsedTask(string requirement)
    {
        return new ParsedTask
        {
            RepositoryUrl = "https://github.com/example-company/user-service",
            BaseBranch = "main",
            Requirement = requirement,
            AcceptanceCriteria = new[]
            {
                "Add or update unit tests"
            }
        };
    }

    private sealed class TemporaryRepository : IDisposable
    {
        public TemporaryRepository()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "repo-analyzer-tests", Guid.NewGuid().ToString("N"));
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
