using System.Text;
using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Infrastructure.Ai;

internal enum PromptIntent
{
    Generate,
    Fix,
    Format
}

internal readonly record struct PromptRuleContext(
    string Language,
    string Framework,
    string TestFramework,
    IReadOnlyList<string> AvailableTestLibraries,
    PromptIntent Intent);

internal interface IPromptRuleBuilder
{
    bool CanHandle(PromptRuleContext context);

    void AppendRules(StringBuilder builder, PromptRuleContext context);
}

internal sealed class DotNetPromptRuleBuilder : IPromptRuleBuilder
{
    public bool CanHandle(PromptRuleContext context)
    {
        return string.Equals(context.Language, "C#", StringComparison.OrdinalIgnoreCase) ||
               context.Framework.Contains("ASP.NET", StringComparison.OrdinalIgnoreCase) ||
               context.Framework.Contains(".NET", StringComparison.OrdinalIgnoreCase) ||
               context.Framework.Contains("NET Core", StringComparison.OrdinalIgnoreCase) ||
               context.Framework.Contains("ASP.NET Core", StringComparison.OrdinalIgnoreCase);
    }

    public void AppendRules(StringBuilder builder, PromptRuleContext context)
    {
        builder.AppendLine("For C# and .NET files, return buildable code that matches the repository's namespace, using, and file organization style.");
        builder.AppendLine("Preserve the repository's existing architectural and naming conventions.");
        builder.AppendLine("Generated or modified files must compile inside the repository without requiring hidden assumptions.");
        builder.AppendLine("Generated or modified class files must include explicit required using directives even if implicit, global, SDK-default, or project-level imports may exist.");
        builder.AppendLine("Include all required using directives for newly referenced types, attributes, LINQ APIs, extension methods, tasks, ASP.NET Core APIs, and test helpers.");
        builder.AppendLine("If you use any external, framework, NuGet, or third-party library type, attribute, helper, extension method, or namespace member, add the explicit using directive in that file for the owning namespace.");
        builder.AppendLine("Do not assume external library namespaces are already available through implicit usings, global usings, project-level usings, SDK defaults, or other files.");
        builder.AppendLine("Do not rely on hidden imports, implicit usings, global usings, or project-level usings unless you are preserving an existing unchanged file.");
        builder.AppendLine("Preserve the file's existing namespace style, including file-scoped or block-scoped namespaces.");
        builder.AppendLine("For generated or modified test files, always include explicit file-level using directives for the test framework actually used by the generated code.");
        builder.AppendLine("Do not wait for the repository test framework setting before adding test framework usings; infer the required using from the attributes, assertions, and helpers you emit.");
        builder.AppendLine("If the generated or modified test code uses xUnit symbols such as Fact, Theory, InlineData, MemberData, ClassData, Trait, Assert, IClassFixture, Collection, CollectionDefinition, or BeforeAfterTestAttribute, include explicit using Xunit; in that file.");
        builder.AppendLine("If the generated or modified test code uses NUnit symbols such as Test, TestCase, TestFixture, SetUp, TearDown, OneTimeSetUp, OneTimeTearDown, Category, Assert, or Is, include explicit using NUnit.Framework; in that file.");
        builder.AppendLine("If the generated or modified test code uses MSTest symbols such as TestClass, TestMethod, DataTestMethod, DataRow, TestInitialize, TestCleanup, ClassInitialize, ClassCleanup, or Assert, include explicit using Microsoft.VisualStudio.TestTools.UnitTesting; in that file.");
        builder.AppendLine("If the generated or modified test code uses mocking, fluent assertions, ASP.NET Core testing, or other test helper libraries, include their explicit using directives in the same file.");
        builder.AppendLine("Generated or modified test files must compile without relying on GlobalUsings.cs, project-level <Using /> items, implicit usings, SDK defaults, or imports from other files.");
        builder.AppendLine("If generated or modified test code uses any third-party test, mocking, assertion, data generation, or ASP.NET Core testing library such as Moq, NSubstitute, FluentAssertions, Shouldly, Bogus, AutoFixture, WebApplicationFactory, or TestServer, the target project must already reference that package or the same response must add the required PackageReference.");
        builder.AppendLine("Introducing a third-party test library namespace, symbol, helper, or API without the corresponding project reference is invalid output.");
        builder.AppendLine("Do not introduce a new third-party test helper, mocking, or assertion library by default.");
        builder.AppendLine("Prefer the repository's existing test libraries and conventions.");
        if (context.AvailableTestLibraries.Count > 0)
        {
            builder.AppendLine($"The repository's available test libraries are: {string.Join(", ", context.AvailableTestLibraries)}.");
            builder.AppendLine("Existing test libraries only is the default. Do not introduce a different mocking, assertion, or test helper library unless the task explicitly requires it.");
        }

        builder.AppendLine("Write the smallest test set that satisfies the task and acceptance criteria.");
        builder.AppendLine("Prefer minimal, repository-style unit tests over broader or more complex test scaffolding.");
        builder.AppendLine("Copy the repository's existing test pattern instead of inventing new fixtures, builders, helpers, abstractions, or test utilities.");
        builder.AppendLine("Do not add unnecessary mocks, fixture setup, test data factories, fluent assertion layers, or integration harnesses.");
        builder.AppendLine("Keep generated tests compile-safe, local-context-driven, and directly focused on the changed behavior.");
        builder.AppendLine("Unless the repository clearly uses integration-style tests for the same area, prefer simple unit tests.");
        builder.AppendLine("Do not add controller unit tests by default.");
        builder.AppendLine("Prefer testing service, application, domain, validator, handler, and other business-logic-heavy layers instead of thin HTTP controllers.");
        builder.AppendLine("Do not write tests for thin pass-through controllers that only delegate to existing services or handlers.");
        builder.AppendLine("Add a controller test only when the controller itself contains meaningful branching, custom HTTP behavior, authorization or result translation logic, or other non-trivial endpoint behavior that is not already covered elsewhere.");
        builder.AppendLine("If endpoint coverage is required and the repository already uses integration-style HTTP tests for that area, prefer following that existing integration pattern over adding new controller unit tests.");
        if (context.Intent == PromptIntent.Fix)
        {
            builder.AppendLine("Do not respond to failing .NET endpoint work by adding new controller unit tests unless the controller behavior itself is the bug that must be fixed.");
        }

        builder.AppendLine("If the repository already contains a .sln or .slnx file and you create a new .csproj, updating the existing solution membership is mandatory.");
        builder.AppendLine("Creating a new .csproj in a repository that already contains a .sln or .slnx file REQUIRES updating that solution file in the same change.");
        builder.AppendLine("A newly created project is considered incomplete until it is added to the existing solution.");
        builder.AppendLine("Never leave a generated .csproj detached from the repository solution.");
        builder.AppendLine("When a new project is created, updating the .sln or .slnx file is part of the same task, not an optional follow-up step.");
        builder.AppendLine("If solution membership cannot be updated, explicitly report the task as incomplete instead of silently omitting the solution change.");
        builder.AppendLine("When a solution file exists, generating a new project without updating solution membership is considered a broken repository state.");
        builder.AppendLine("Repository integrity is more important than partial code generation.");
        builder.AppendLine("A repository with a detached .csproj is considered invalid output.");
        builder.AppendLine("Do not finish the task until the new project is part of the existing solution.");
        builder.AppendLine("Solution membership updates are mandatory repository modifications, not optional maintenance tasks.");
        builder.AppendLine("Do not create a second solution file unless explicitly requested.");
        builder.AppendLine("When adding a new project to an existing solution, preserve the repository's existing solution structure.");
        builder.AppendLine("Preserve existing solution folders, project nesting, build configurations, platform mappings, and GUID structure.");
        builder.AppendLine("If the repository uses .slnx, preserve the existing .slnx structure and include the new project entry.");
        builder.AppendLine("Do not replace or regenerate the entire solution file when only adding a new project.");
        builder.AppendLine("Modify the existing solution minimally and safely.");
        builder.AppendLine("If a new test project is created, ensure it is added under the repository's existing tests folder structure and existing solution organization conventions.");
        builder.AppendLine("When generating a new test project, include all required ProjectReference entries for the production projects being tested.");
        builder.AppendLine("Ensure referenced project paths match the repository folder structure.");
        builder.AppendLine("Do not generate dangling ProjectReference entries.");
        builder.AppendLine("Ensure generated projects restore and build correctly inside the existing solution.");
        builder.AppendLine("When generating a new .csproj, include all required PackageReference entries for the generated code.");
        builder.AppendLine("Do not omit required test, mocking, assertion, or ASP.NET Core testing packages.");
        builder.AppendLine("Ensure package versions follow existing repository conventions when discoverable.");
        builder.AppendLine("Do not introduce conflicting package versions unless explicitly required.");
        builder.AppendLine("All generated code, project files, and solution modifications must result in a buildable repository state.");
        builder.AppendLine("Do not generate placeholder solution entries, fake GUIDs, invalid project paths, or incomplete project configurations.");
        builder.AppendLine("Generated repository changes must be internally consistent.");

        if (string.Equals(context.TestFramework, "xUnit", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("The repository test framework is xUnit.");
            builder.AppendLine("xUnit test files must compile.");
            builder.AppendLine("Generated or modified xUnit test classes must include explicit using Xunit; in the file.");
            builder.AppendLine("Ensure the project resolves Fact, Theory, InlineData, MemberData, ClassData, Trait, Assert, IClassFixture, Collection, and CollectionDefinition when those symbols are used.");
            builder.AppendLine("If you create a new xUnit test project, include Microsoft.NET.Test.Sdk, xunit, xunit.runner.visualstudio, coverlet.collector if coverage tooling is already used, and set IsTestProject=true.");
            builder.AppendLine("New xUnit test projects must restore and execute successfully through the existing solution.");
            builder.AppendLine("Do not generate xUnit tests without ensuring the project has the required xUnit package references.");
            builder.AppendLine("If the repository already uses a specific mocking or assertion library, reuse it instead of introducing a different xUnit-side helper stack.");
        }
        else if (string.Equals(context.TestFramework, "NUnit", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("The repository test framework is NUnit.");
            builder.AppendLine("Generated or modified NUnit test classes must include explicit using NUnit.Framework; in the file.");
            builder.AppendLine("Ensure the project resolves Test, TestCase, TestFixture, SetUp, TearDown, Assert, and Is when those symbols are used.");
            builder.AppendLine("If you create a new NUnit test project, include Microsoft.NET.Test.Sdk, NUnit, NUnit3TestAdapter, and set IsTestProject=true.");
            builder.AppendLine("Do not generate NUnit tests without ensuring the project has the required NUnit package references.");
            builder.AppendLine("If the repository already uses a specific mocking or assertion library, reuse it instead of introducing a different NUnit-side helper stack.");
        }
        else if (string.Equals(context.TestFramework, "MSTest", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("The repository test framework is MSTest.");
            builder.AppendLine("Generated or modified MSTest test classes must include explicit using Microsoft.VisualStudio.TestTools.UnitTesting; in the file.");
            builder.AppendLine("Ensure the project resolves TestClass, TestMethod, DataTestMethod, DataRow, TestInitialize, TestCleanup, and Assert when those symbols are used.");
            builder.AppendLine("If you create a new MSTest test project, include Microsoft.NET.Test.Sdk, MSTest.TestFramework, MSTest.TestAdapter, and set IsTestProject=true.");
            builder.AppendLine("Do not generate MSTest tests without ensuring the project has the required MSTest package references.");
            builder.AppendLine("If the repository already uses a specific mocking or assertion library, reuse it instead of introducing a different MSTest-side helper stack.");
        }
        else
        {
            builder.AppendLine("The repository test framework is unknown or not specified.");
            builder.AppendLine("When generating tests, choose the framework that best matches the repository conventions.");
            builder.AppendLine("Always include the explicit file-level using directive for the selected test framework.");
            builder.AppendLine("Never emit unqualified test attributes or assertions without also emitting the using directive that defines them.");
            builder.AppendLine("If a new test project is created while the repository already contains a solution file, ensure the new project is included in the existing solution membership.");
            builder.AppendLine("Do not generate incomplete or detached test projects.");
        }
    }
}

internal sealed class AspNetCorePromptRuleBuilder : IPromptRuleBuilder
{
    public bool CanHandle(PromptRuleContext context)
    {
        return context.Framework.Contains("ASP.NET", StringComparison.OrdinalIgnoreCase);
    }

    public void AppendRules(StringBuilder builder, PromptRuleContext context)
    {
        builder.AppendLine("For ASP.NET Core code, preserve the existing controller, endpoint, middleware, and minimal API conventions used in the repository.");
        builder.AppendLine("Ensure DTO, request, response, validator, service, repository, MediatR, AutoMapper, and dependency injection references are correctly resolved with the proper namespaces.");
        builder.AppendLine("If you introduce MVC attributes, HTTP attributes, IActionResult types, Results helpers, filters, middleware, endpoint mappings, or DI services, include the correct Microsoft.AspNetCore and Microsoft.Extensions namespaces when needed.");
        builder.AppendLine("Preserve the repository's dependency injection registration patterns.");
        builder.AppendLine("Do not introduce conflicting ASP.NET Core architectural styles unless explicitly requested.");
        builder.AppendLine("Ensure generated ASP.NET Core code compiles against the repository's target framework and package ecosystem.");
        builder.AppendLine("If generating integration tests for ASP.NET Core applications, include all required ASP.NET Core testing package references and namespaces.");
        builder.AppendLine("If WebApplicationFactory, TestServer, Minimal APIs, endpoint routing, or authentication testing helpers are used, ensure the required package references and using directives are included.");
        builder.AppendLine("Do not assume implicit ASP.NET Core usings are available.");
        builder.AppendLine("Generated ASP.NET Core endpoints, controllers, middleware, filters, and services must integrate cleanly into the repository's existing startup and DI structure.");
        builder.AppendLine("Do not add unit tests for thin controllers or thin route handlers by default.");
        builder.AppendLine("Do not add controller tests just because a new route or action exists.");
        builder.AppendLine("When ASP.NET endpoint behavior needs tests, prefer the repository's existing integration, end-to-end, or HTTP-level test pattern when one already exists.");
    }
}

internal sealed class JavaScriptPromptRuleBuilder : IPromptRuleBuilder
{
    public bool CanHandle(PromptRuleContext context)
    {
        return context.Language.Contains("JavaScript", StringComparison.OrdinalIgnoreCase) ||
               context.Language.Contains("TypeScript", StringComparison.OrdinalIgnoreCase) ||
               context.Framework.Contains("Node", StringComparison.OrdinalIgnoreCase);
    }

    public void AppendRules(StringBuilder builder, PromptRuleContext context)
    {
        builder.AppendLine("For JavaScript or TypeScript files, preserve the existing module system and import style used by the repository.");
        builder.AppendLine("If you reference a new symbol, add the required import or require statement and keep relative import paths consistent with nearby files.");
        builder.AppendLine("Do not invent new package dependencies unless they are already evident in the repository context.");
    }
}

internal sealed class JavaPromptRuleBuilder : IPromptRuleBuilder
{
    public bool CanHandle(PromptRuleContext context)
    {
        return string.Equals(context.Language, "Java", StringComparison.OrdinalIgnoreCase) ||
               context.Language.Contains("Kotlin", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(context.Framework, "Java", StringComparison.OrdinalIgnoreCase) ||
               context.Framework.Contains("Spring", StringComparison.OrdinalIgnoreCase);
    }

    public void AppendRules(StringBuilder builder, PromptRuleContext context)
    {
        builder.AppendLine("For Java or Kotlin files, preserve the existing package declaration and add all required import statements for newly referenced types, annotations, or framework classes.");
        builder.AppendLine("Match the repository's existing class, package, and framework conventions.");
    }
}

internal sealed class GoPromptRuleBuilder : IPromptRuleBuilder
{
    public bool CanHandle(PromptRuleContext context)
    {
        return string.Equals(context.Language, "Go", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(context.Framework, "Go", StringComparison.OrdinalIgnoreCase);
    }

    public void AppendRules(StringBuilder builder, PromptRuleContext context)
    {
        builder.AppendLine("For Go files, keep the correct package declaration and update import blocks so all referenced symbols resolve.");
        builder.AppendLine("Do not leave unused imports behind.");
    }
}

internal sealed class PythonPromptRuleBuilder : IPromptRuleBuilder
{
    public bool CanHandle(PromptRuleContext context)
    {
        return string.Equals(context.Language, "Python", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(context.Framework, "Python", StringComparison.OrdinalIgnoreCase);
    }

    public void AppendRules(StringBuilder builder, PromptRuleContext context)
    {
        builder.AppendLine("For Python files, preserve the repository's import style and add any required imports for newly referenced symbols.");
        builder.AppendLine("Do not introduce unused imports.");
    }
}

internal sealed class FallbackPromptRuleBuilder : IPromptRuleBuilder
{
    public bool CanHandle(PromptRuleContext context)
    {
        return true;
    }

    public void AppendRules(StringBuilder builder, PromptRuleContext context)
    {
        builder.AppendLine("Return buildable code for the target language and add any required import, using, include, package, or module references needed by newly introduced symbols.");
    }
}

internal static class PromptRuleBuilderFactory
{
    private static readonly IReadOnlyList<IPromptRuleBuilder> Builders =
    [
        new DotNetPromptRuleBuilder(),
        new AspNetCorePromptRuleBuilder(),
        new JavaScriptPromptRuleBuilder(),
        new JavaPromptRuleBuilder(),
        new GoPromptRuleBuilder(),
        new PythonPromptRuleBuilder(),
        new FallbackPromptRuleBuilder()
    ];

    public static string BuildLanguageSpecificRules(PromptRuleContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine("Produce repository-ready code, not pseudocode.");
        builder.AppendLine("Every changed file must be internally consistent and ready to write to disk.");

        if (context.Intent is PromptIntent.Generate or PromptIntent.Fix)
        {
            builder.AppendLine("If you reference a new type, function, attribute, decorator, annotation, extension method, include, import, package, or module, add the required import/using/include statement or use a fully qualified reference when appropriate.");
            builder.AppendLine("Do not rely on hidden imports or implicit symbols unless they are clearly evident from the provided repository context.");
        }

        foreach (var ruleBuilder in Builders.Where(item => item.CanHandle(context)))
        {
            ruleBuilder.AppendRules(builder, context);
        }

        return builder.ToString().TrimEnd();
    }
}
