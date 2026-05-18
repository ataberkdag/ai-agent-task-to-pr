using AiAgentChallenge.Domain;
using AiAgentChallenge.Infrastructure.Ai;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class AiCodeAgentSharedTests
{
    [Fact]
    public void BuildSystemPrompt_ForAspNetCore_IncludesDotNetRules()
    {
        var prompt = AiCodeAgentShared.BuildSystemPrompt(CreateContext("C#", "ASP.NET Core", "xUnit", "xUnit, Moq, FluentAssertions"));

        Assert.Contains("Include all required using directives", prompt);
        Assert.Contains("external, framework, NuGet, or third-party library", prompt);
        Assert.Contains("Do not assume external library namespaces are already available", prompt);
        Assert.Contains("implicit, global, SDK-default, or project-level imports", prompt);
        Assert.Contains("Preserve the file's existing namespace style", prompt);
        Assert.Contains("Existing constructors and methods must be called with the exact parameter order defined in source", prompt);
        Assert.Contains("If a constructor or method signature is included in the provided context, use that signature exactly", prompt);
        Assert.Contains("ASP.NET Core", prompt);
        Assert.Contains("Microsoft.AspNetCore", prompt);
        Assert.Contains("using Xunit;", prompt);
        Assert.Contains("Ensure the project resolves Fact, Theory, InlineData", prompt);
        Assert.Contains("mocking, fluent assertions, ASP.NET Core testing, or other test helper libraries", prompt);
        Assert.Contains("must include explicit using Xunit;", prompt);
        Assert.Contains("Generated or modified test files must compile without relying on GlobalUsings.cs", prompt);
        Assert.Contains("Introducing a third-party test library namespace, symbol, helper, or API without the corresponding project reference is invalid output", prompt);
        Assert.Contains("The repository's available test libraries are: xUnit, Moq, FluentAssertions.", prompt);
        Assert.Contains("Existing test libraries only is the default", prompt);
        Assert.Contains("Write the smallest test set that satisfies the task and acceptance criteria", prompt);
        Assert.Contains("Do not add unnecessary mocks, fixture setup, test data factories, fluent assertion layers, or integration harnesses", prompt);
        Assert.Contains("If the repository already contains a .sln or .slnx file and you create a new .csproj", prompt);
        Assert.Contains("Never leave a generated .csproj detached from the repository solution", prompt);
    }

    [Fact]
    public void BuildFixSystemPrompt_ForJava_IncludesImportRules()
    {
        var prompt = AiCodeAgentShared.BuildFixSystemPrompt(
            CreateContext("Java", "Spring Boot", "Unknown"),
            null,
            new TestResult { Status = TestExecutionStatus.Failed });

        Assert.Contains("package declaration", prompt);
        Assert.Contains("required import statements", prompt);
        Assert.Contains("Existing constructors and methods must be called with the exact parameter order defined in source", prompt);
        Assert.Contains("Use the Critical Signatures listed in Repository Analysis Summary exactly as written", prompt);
        Assert.Contains("Fix only the failing test logic or the production behavior required by the failing tests", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_ForNode_IncludesModuleSystemRules()
    {
        var prompt = AiCodeAgentShared.BuildSystemPrompt(CreateContext("JavaScript/TypeScript", "Node.js", "Unknown"));

        Assert.Contains("module system", prompt);
        Assert.Contains("import or require statement", prompt);
        Assert.Contains("Do not invent new package dependencies", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_ForGo_IncludesImportAndPackageRules()
    {
        var prompt = AiCodeAgentShared.BuildSystemPrompt(CreateContext("Go", "Go", "Unknown"));

        Assert.Contains("package declaration", prompt);
        Assert.Contains("update import blocks", prompt);
        Assert.Contains("Do not leave unused imports behind", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_ForPython_IncludesImportRules()
    {
        var prompt = AiCodeAgentShared.BuildSystemPrompt(CreateContext("Python", "Python", "Unknown"));

        Assert.Contains("preserve the repository's import style", prompt);
        Assert.Contains("Do not introduce unused imports", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_ForUnknownLanguage_IncludesFallbackRules()
    {
        var prompt = AiCodeAgentShared.BuildSystemPrompt(CreateContext("Unknown", "Unknown", "Unknown"));

        Assert.Contains("Return buildable code for the target language", prompt);
        Assert.Contains("required import, using, include, package, or module references", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_ForDotNetWithUnknownTestFramework_IncludesSolutionMembershipRules()
    {
        var prompt = AiCodeAgentShared.BuildSystemPrompt(CreateContext("C#", ".NET", "Unknown"));

        Assert.Contains("If the repository already contains a .sln or .slnx file and you create a new .csproj", prompt);
        Assert.Contains("Never leave a generated .csproj detached from the repository solution", prompt);
        Assert.Contains("If a new test project is created while the repository already contains a solution file", prompt);
    }

    [Fact]
    public void BuildFormattingRegenerationPrompt_RemainsFormatOnly()
    {
        var prompt = AiCodeAgentShared.BuildFormattingRegenerationPrompt();

        Assert.Contains("Reformat the previously generated files only", prompt);
        Assert.Contains("Do not add or remove imports, usings, includes, package declarations, or dependencies", prompt);
        Assert.DoesNotContain("Include all required using directives", prompt);
        Assert.DoesNotContain("using Xunit;", prompt);
    }

    [Fact]
    public void BuildFixContext_SeparatesBuildDiagnosticsAndFailingTestOutput()
    {
        var context = AiCodeAgentShared.BuildFixContext(
            CreateContext("C#", ".NET", "xUnit"),
            new AiCodeChangeResult
            {
                Summary = "Initial change",
                ChangedFiles = new[]
                {
                    new AiChangedFile
                    {
                        Path = "src/App/File.cs",
                        Operation = "modify",
                        Content = "content"
                    }
                }
            },
            new BuildResult
            {
                Command = "dotnet build App.sln",
                Status = BuildExecutionStatus.Failed,
                ExitCode = 1,
                Stdout = "build stdout",
                Stderr = "build stderr",
                AttemptNumber = 1
            },
            new TestResult
            {
                Command = "dotnet test",
                Status = TestExecutionStatus.Failed,
                ExitCode = 1,
                Stdout = "test stdout",
                Stderr = "test stderr",
                AttemptNumber = 1
            });

        Assert.Contains("Build Diagnostics:", context.TaskSummary);
        Assert.Contains("Command: dotnet build App.sln", context.TaskSummary);
        Assert.Contains("build stderr", context.TaskSummary);
        Assert.Contains("Failing Test Output:", context.TaskSummary);
        Assert.Contains("Command: dotnet test", context.TaskSummary);
        Assert.Contains("test stderr", context.TaskSummary);
    }

    [Fact]
    public void BuildFixSystemPrompt_WhenBuildFailed_NarrowsToBuildIssues()
    {
        var prompt = AiCodeAgentShared.BuildFixSystemPrompt(
            CreateContext("C#", ".NET", "xUnit", "xUnit, Moq"),
            new BuildResult { Status = BuildExecutionStatus.Failed },
            new TestResult { Status = TestExecutionStatus.Failed });

        Assert.Contains("Fix build, compile, package reference, project reference, solution membership, missing using, and target framework issues only", prompt);
        Assert.Contains("Do not redesign, broaden, or rewrite tests unless that is strictly required to resolve the build failure", prompt);
    }

    private static AgentContext CreateContext(string language, string framework, string testFramework, string availableTestLibraries = "")
    {
        var repositorySummary = $"Language: {language}\nFramework: {framework}\nDetected Test Framework: {testFramework}";
        if (!string.IsNullOrWhiteSpace(availableTestLibraries))
        {
            repositorySummary += $"\nAvailable Test Libraries: {availableTestLibraries}";
        }

        return new AgentContext
        {
            Language = language,
            Framework = framework,
            TestFramework = testFramework,
            TaskSummary = "Requirement: Update endpoint",
            RepositoryAnalysisSummary = repositorySummary,
            SelectedFiles = Array.Empty<AgentContextFile>()
        };
    }
}
