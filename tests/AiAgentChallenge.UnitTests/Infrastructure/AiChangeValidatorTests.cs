using AiAgentChallenge.Domain;
using AiAgentChallenge.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class AiChangeValidatorTests
{
    [Fact]
    public void Validate_RejectsAbsolutePath()
    {
        var result = CreateValidator().Validate(
            @"C:\repo",
            CreateAnalysis(),
            CreateResult(new AiChangedFile { Path = @"C:\temp\file.cs", Operation = "modify", Content = "test" }));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Validate_RejectsPathTraversal()
    {
        var result = CreateValidator().Validate(
            @"C:\repo",
            CreateAnalysis(),
            CreateResult(new AiChangedFile { Path = "../outside.cs", Operation = "modify", Content = "test" }));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Validate_RejectsSensitiveFileChange()
    {
        var result = CreateValidator().Validate(
            @"C:\repo",
            CreateAnalysis(),
            CreateResult(new AiChangedFile { Path = ".env", Operation = "modify", Content = "test" }));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Validate_RejectsEmptyChangedFiles()
    {
        var result = CreateValidator().Validate(
            @"C:\repo",
            CreateAnalysis(),
            new AiCodeChangeResult { Summary = "summary", ChangedFiles = Array.Empty<AiChangedFile>(), TestNotes = "notes" });

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Validate_RejectsInvalidOperation()
    {
        var result = CreateValidator().Validate(
            @"C:\repo",
            CreateAnalysis(),
            CreateResult(new AiChangedFile { Path = "src/File.cs", Operation = "delete", Content = "test" }));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Validate_RejectsCollapsedSingleLineCode()
    {
        var result = CreateValidator().Validate(
            @"C:\repo",
            CreateAnalysis(),
            CreateResult(new AiChangedFile
            {
                Path = "src/File.cs",
                Operation = "modify",
                Content = "namespace Demo { public class RegisterService { public void Execute() { var value = 1; if (value > 0) { value++; } Console.WriteLine(value); } } }"
            }));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.Contains("collapsed source content", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_AllowsShortSingleLineCodeWithoutFalsePositive()
    {
        var result = CreateValidator().Validate(
            @"C:\repo",
            CreateAnalysis(),
            CreateResult(new AiChangedFile
            {
                Path = "src/File.cs",
                Operation = "modify",
                Content = "public class File {}"
            }));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_AllowsNonCodeFilesWithoutCollapsedSourceFailure()
    {
        var result = CreateValidator().Validate(
            @"C:\repo",
            new RepositoryAnalysis
            {
                RelevantFiles = new[] { "docs/output.json" }
            },
            CreateResult(new AiChangedFile
            {
                Path = "docs/output.json",
                Operation = "modify",
                Content = "{\"key\":\"value\",\"items\":[1,2,3]}"
            }));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_RejectsXunitTestWithoutUsingOrProjectLevelSupport()
    {
        var result = CreateValidator().Validate(
            @"C:\repo",
            new RepositoryAnalysis
            {
                Language = "C#",
                Framework = ".NET",
                TestFramework = "xUnit",
                ExistingTestFiles = new[] { "tests/Users/RegisterServiceTests.cs" }
            },
            CreateResult(new AiChangedFile
            {
                Path = "tests/Users/RegisterServiceTests.cs",
                Operation = "modify",
                Content = """
                    namespace Demo.Tests;

                    public sealed class RegisterServiceTests
                    {
                        [Fact]
                        public void Registers_user()
                        {
                            Assert.True(true);
                        }
                    }
                    """
            }));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.Contains("using Xunit;", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_AllowsXunitTestWithExplicitUsing()
    {
        var result = CreateValidator().Validate(
            @"C:\repo",
            new RepositoryAnalysis
            {
                Language = "C#",
                Framework = ".NET",
                TestFramework = "xUnit",
                ExistingTestFiles = new[] { "tests/Users/RegisterServiceTests.cs" }
            },
            CreateResult(new AiChangedFile
            {
                Path = "tests/Users/RegisterServiceTests.cs",
                Operation = "modify",
                Content = """
                    using Xunit;

                    namespace Demo.Tests;

                    public sealed class RegisterServiceTests
                    {
                        [Fact]
                        public void Registers_user()
                        {
                            Assert.True(true);
                        }
                    }
                    """
            }));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_RejectsXunitTestWhenOnlyProjectLevelUsingExists()
    {
        using var repo = new TemporaryRepository();
        repo.AddFile("tests/App.Tests/App.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="xunit" Version="2.5.3" />
              </ItemGroup>
              <ItemGroup>
                <Using Include="Xunit" />
              </ItemGroup>
            </Project>
            """);

        var result = CreateValidator().Validate(
            repo.RootPath,
            new RepositoryAnalysis
            {
                Language = "C#",
                Framework = ".NET",
                TestFramework = "xUnit",
                ProjectFiles = new[] { "tests/App.Tests/App.Tests.csproj" },
                ExistingTestFiles = new[] { "tests/Users/RegisterServiceTests.cs" }
            },
            CreateResult(new AiChangedFile
            {
                Path = "tests/Users/RegisterServiceTests.cs",
                Operation = "modify",
                Content = """
                    namespace Demo.Tests;

                    public sealed class RegisterServiceTests
                    {
                        [Fact]
                        public void Registers_user()
                        {
                            Assert.True(true);
                        }
                    }
                    """
            }));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.Contains("explicit 'using Xunit;'", StringComparison.OrdinalIgnoreCase));
    }

    private static AiChangeValidator CreateValidator()
    {
        return new AiChangeValidator(Options.Create(new AiOptions
        {
            MaxChangedFiles = 5
        }));
    }

    private static RepositoryAnalysis CreateAnalysis()
    {
        return new RepositoryAnalysis
        {
            RelevantFiles = new[] { "src/File.cs" }
        };
    }

    private static AiCodeChangeResult CreateResult(AiChangedFile file)
    {
        return new AiCodeChangeResult
        {
            Summary = "summary",
            ChangedFiles = new[] { file },
            TestNotes = "notes"
        };
    }

    private sealed class TemporaryRepository : IDisposable
    {
        public TemporaryRepository()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "ai-change-validator-tests", Guid.NewGuid().ToString("N"));
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
