using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Application.Tasks;
using AiAgentChallenge.Domain;
using AiAgentChallenge.Infrastructure.Git;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.Infrastructure.Projects;

public sealed class DotNetSolutionProjectSynchronizer : ISolutionProjectSynchronizer
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        "bin",
        "obj",
        "node_modules",
        "dist",
        "build",
        "target",
        "coverage",
        ".idea",
        ".vscode"
    };

    private readonly IProcessRunner _processRunner;
    private readonly GitOptions _gitOptions;
    private readonly ILogger<DotNetSolutionProjectSynchronizer> _logger;

    public DotNetSolutionProjectSynchronizer(
        IProcessRunner processRunner,
        IOptions<GitOptions> gitOptions,
        ILogger<DotNetSolutionProjectSynchronizer> logger)
    {
        _processRunner = processRunner;
        _gitOptions = gitOptions.Value;
        _logger = logger;
    }

    public async Task<DotNetSolutionBaseline> CaptureBaselineAsync(
        string repositoryPath,
        RepositoryAnalysis repositoryAnalysis,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(repositoryAnalysis.BuildTool, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return DotNetSolutionBaseline.Unsupported("Solution sync skipped because the repository is not a .NET solution.");
        }

        var primarySolution = repositoryAnalysis.ProjectFiles
            .FirstOrDefault(path => path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            ?? repositoryAnalysis.ProjectFiles
                .FirstOrDefault(path => path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(primarySolution))
        {
            return DotNetSolutionBaseline.Unsupported("Solution sync skipped because no solution file was detected.");
        }

        if (!TryResolveSafePath(repositoryPath, primarySolution, out var solutionPath) || !File.Exists(solutionPath))
        {
            return DotNetSolutionBaseline.Failure($"Primary solution file '{primarySolution}' could not be found inside the repository.");
        }

        var listResult = await RunDotNetAsync(
            solutionPath,
            new[] { "sln", solutionPath, "list" },
            cancellationToken);

        if (listResult.TimedOut)
        {
            return DotNetSolutionBaseline.Failure("Timed out while capturing the solution project baseline.");
        }

        if (listResult.ExitCode != 0)
        {
            return DotNetSolutionBaseline.Failure("Failed to capture the solution project baseline.");
        }

        var solutionMembers = ParseSolutionListOutput(repositoryPath, solutionPath, listResult.StandardOutput);
        var existingProjects = EnumerateCurrentProjects(repositoryPath);

        return DotNetSolutionBaseline.Success(solutionPath, existingProjects, solutionMembers);
    }

    public async Task<DotNetSolutionSyncResult> SyncAsync(
        string repositoryPath,
        DotNetSolutionBaseline baseline,
        CancellationToken cancellationToken = default)
    {
        if (!baseline.IsSupported)
        {
            return DotNetSolutionSyncResult.Success(
                baseline.SolutionPath,
                Array.Empty<string>(),
                Array.Empty<string>(),
                baseline.Message);
        }

        if (!baseline.IsSuccess)
        {
            return DotNetSolutionSyncResult.Failure(baseline.SolutionPath, baseline.Message);
        }

        var currentProjects = EnumerateCurrentProjects(repositoryPath);
        var baselineExisting = baseline.ExistingProjectFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var baselineMembers = baseline.BaselineSolutionMembers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentProjectSet = currentProjects.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var newProjects = currentProjects
            .Where(path => !baselineExisting.Contains(path))
            .ToArray();

        var removedProjects = baseline.BaselineSolutionMembers
            .Where(path => !currentProjectSet.Contains(path))
            .ToArray();

        _logger.LogInformation(
            "Running solution sync for {SolutionPath} with {AddedProjectCount} added and {RemovedProjectCount} removed project(s). AddedProjects={AddedProjects}; RemovedProjects={RemovedProjects}",
            baseline.SolutionPath,
            newProjects.Length,
            removedProjects.Length,
            string.Join(", ", newProjects),
            string.Join(", ", removedProjects));

        foreach (var project in newProjects)
        {
            if (!TryResolveSafePath(repositoryPath, project, out var projectPath))
            {
                return DotNetSolutionSyncResult.Failure(baseline.SolutionPath, $"Project '{project}' could not be resolved inside the repository.");
            }

            var addResult = await RunDotNetAsync(
                baseline.SolutionPath,
                new[] { "sln", baseline.SolutionPath, "add", projectPath },
                cancellationToken);

            if (addResult.TimedOut)
            {
                return DotNetSolutionSyncResult.Failure(baseline.SolutionPath, $"Timed out while adding project '{project}' to the solution.");
            }

            if (addResult.ExitCode != 0)
            {
                return DotNetSolutionSyncResult.Failure(baseline.SolutionPath, $"Failed to add project '{project}' to the solution.");
            }
        }

        foreach (var project in removedProjects)
        {
            var removeResult = await RunDotNetAsync(
                baseline.SolutionPath,
                new[] { "sln", baseline.SolutionPath, "remove", project },
                cancellationToken);

            if (removeResult.TimedOut)
            {
                return DotNetSolutionSyncResult.Failure(baseline.SolutionPath, $"Timed out while removing project '{project}' from the solution.");
            }

            if (removeResult.ExitCode != 0)
            {
                return DotNetSolutionSyncResult.Failure(baseline.SolutionPath, $"Failed to remove project '{project}' from the solution.");
            }
        }

        return DotNetSolutionSyncResult.Success(
            baseline.SolutionPath,
            newProjects,
            removedProjects,
            $"Solution sync completed. Added: {newProjects.Length}, Removed: {removedProjects.Length}.");
    }

    private async Task<ProcessExecutionResult> RunDotNetAsync(
        string solutionPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        return await _processRunner.RunAsync(
            new ProcessExecutionRequest
            {
                FileName = "dotnet",
                WorkingDirectory = Path.GetDirectoryName(solutionPath) ?? Directory.GetCurrentDirectory(),
                Arguments = arguments,
                Timeout = TimeSpan.FromSeconds(Math.Max(1, _gitOptions.PushTimeoutSeconds))
            },
            cancellationToken);
    }

    private static IReadOnlyList<string> ParseSolutionListOutput(
        string repositoryPath,
        string solutionPath,
        string output)
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath) ?? repositoryPath;
        var projects = new List<string>();

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!rawLine.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(solutionDirectory, rawLine));
            projects.Add(Path.GetRelativePath(repositoryPath, fullPath).Replace('\\', '/'));
        }

        return projects
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> EnumerateCurrentProjects(string repositoryPath)
    {
        return EnumerateProjectFiles(repositoryPath)
            .Select(path => Path.GetRelativePath(repositoryPath, path).Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateProjectFiles(string rootPath)
    {
        foreach (var file in Directory.EnumerateFiles(rootPath, "*.csproj"))
        {
            yield return file;
        }

        foreach (var directory in Directory.EnumerateDirectories(rootPath))
        {
            if (IgnoredDirectories.Contains(Path.GetFileName(directory)))
            {
                continue;
            }

            foreach (var file in EnumerateProjectFiles(directory))
            {
                yield return file;
            }
        }
    }

    private static bool TryResolveSafePath(string repositoryPath, string relativePath, out string fullPath)
    {
        fullPath = Path.GetFullPath(Path.Combine(repositoryPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var normalizedRepository = Path.GetFullPath(repositoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(normalizedRepository, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                   normalizedRepository.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                   StringComparison.OrdinalIgnoreCase);
    }
}
