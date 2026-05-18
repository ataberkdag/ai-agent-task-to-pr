using System.Text.RegularExpressions;
using System.Xml.Linq;
using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Infrastructure.Analysis;

public sealed class RuleBasedRepositoryAnalyzer : IRepositoryAnalyzer
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

    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".java",
        ".kt",
        ".ts",
        ".js",
        ".py",
        ".go",
        ".rb",
        ".php"
    };

    private static readonly HashSet<string> ProjectFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "package.json",
        "go.mod",
        "go.work",
        "pom.xml",
        "pyproject.toml",
        "requirements.txt",
        "tsconfig.json",
        "pnpm-workspace.yaml",
        "turbo.json",
        "nx.json",
        "settings.gradle",
        "settings.gradle.kts"
    };

    private static readonly HashSet<string> RootOnlyProjectFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "go.work",
        "tsconfig.json",
        "pnpm-workspace.yaml",
        "turbo.json",
        "nx.json",
        "settings.gradle",
        "settings.gradle.kts"
    };

    private const int MaxResultFiles = 20;
    private const int MaxProjectFiles = 12;
    private const long MaxContentScanBytes = 1024 * 1024;
    private const int MaxDependencyWalkDepth = 3;
    private static readonly IReadOnlyDictionary<string, string> KnownTestLibraries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["xunit"] = "xUnit",
        ["xunit.runner.visualstudio"] = "xUnit Runner",
        ["nunit"] = "NUnit",
        ["mstest.testframework"] = "MSTest",
        ["mstest"] = "MSTest",
        ["microsoft.net.test.sdk"] = "Microsoft.NET.Test.Sdk",
        ["moq"] = "Moq",
        ["nsubstitute"] = "NSubstitute",
        ["fluentassertions"] = "FluentAssertions",
        ["shouldly"] = "Shouldly",
        ["bogus"] = "Bogus",
        ["autofixture"] = "AutoFixture",
        ["coverlet.collector"] = "coverlet.collector",
        ["microsoft.aspnetcore.mvc.testing"] = "Microsoft.AspNetCore.Mvc.Testing",
        ["microsoft.aspnetcore.testhost"] = "Microsoft.AspNetCore.TestHost"
    };

    private readonly IReadOnlyList<IFrameworkAnalyzerStrategy> _strategies;

    public RuleBasedRepositoryAnalyzer()
        : this(new IFrameworkAnalyzerStrategy[]
        {
            new AspNetCoreFrameworkAnalyzer(),
            new NodeJsFrameworkAnalyzer(),
            new GoFrameworkAnalyzer()
        })
    {
    }

    internal RuleBasedRepositoryAnalyzer(IReadOnlyList<IFrameworkAnalyzerStrategy> strategies)
    {
        _strategies = strategies;
    }

    public async Task<RepositoryAnalysis> AnalyzeAsync(
        string repositoryPath,
        ParsedTask parsedTask,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || !Directory.Exists(repositoryPath))
        {
            throw new DirectoryNotFoundException($"Repository path '{repositoryPath}' does not exist.");
        }

        var files = EnumerateFiles(repositoryPath)
            .Select(path => new AnalyzedRepositoryFile(
                path,
                GetRelativePath(repositoryPath, path),
                Path.GetFileName(path)))
            .ToList();

        var detection = DetectProjectType(repositoryPath, files);
        var keywords = ExtractKeywords(parsedTask);
        var genericRelevantFiles = await FindRelevantFilesAsync(parsedTask, files, keywords, cancellationToken);
        var genericTestFiles = FindExistingTestFiles(files);
        var dotNetProjectMetadata = DetectDotNetProjectMetadata(files, detection);
        var availableTestLibraries = DetectAvailableTestLibraries(files, detection);

        var context = new RepositoryScanContext
        {
            RepositoryPath = repositoryPath,
            ParsedTask = parsedTask,
            Detection = detection,
            Files = files,
            Keywords = keywords
        };

        var strategy = _strategies.FirstOrDefault(item => item.CanHandle(detection));
        var strategyResult = strategy is null
            ? new FrameworkAnalysisResult()
            : await strategy.AnalyzeAsync(context, cancellationToken);

        var projectFiles = detection.DetectedProjectFiles
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxProjectFiles)
            .ToArray();

        var relevantFiles = strategyResult.RecommendedFiles
            .Concat(genericRelevantFiles)
            .Where(path => SourceExtensions.Contains(Path.GetExtension(path)) || projectFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        relevantFiles = ExpandRelevantFiles(relevantFiles, strategyResult.Symbols, files, detection)
            .Take(MaxResultFiles)
            .ToArray();

        var existingTestFiles = strategyResult.ExistingTestFiles
            .Concat(genericTestFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxResultFiles)
            .ToArray();

        return new RepositoryAnalysis
        {
            Language = detection.Language,
            Framework = detection.Framework,
            BuildTool = detection.BuildTool,
            TestCommand = detection.TestCommand,
            TestFramework = detection.TestFramework,
            AvailableTestLibraries = availableTestLibraries,
            TargetFramework = dotNetProjectMetadata.TargetFramework,
            TargetFrameworks = dotNetProjectMetadata.TargetFrameworks,
            LangVersion = dotNetProjectMetadata.LangVersion,
            ProjectFiles = projectFiles,
            RelevantFiles = relevantFiles,
            ExistingTestFiles = existingTestFiles,
            ApiEndpoints = strategyResult.ApiEndpoints,
            Symbols = strategyResult.Symbols
        };
    }

    private static DotNetProjectMetadata DetectDotNetProjectMetadata(
        IReadOnlyList<AnalyzedRepositoryFile> files,
        ProjectDetection detection)
    {
        if (!string.Equals(detection.BuildTool, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return DotNetProjectMetadata.Empty;
        }

        var candidateFiles = files
            .Where(file => file.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(file.FileName, "Directory.Build.props", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(file.FileName, "Directory.Build.targets", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var targetFramework = string.Empty;
        IReadOnlyList<string> targetFrameworks = Array.Empty<string>();
        var langVersion = string.Empty;

        foreach (var file in candidateFiles)
        {
            var metadata = TryReadMsBuildMetadata(file.FullPath);
            if (string.IsNullOrWhiteSpace(targetFramework) && !string.IsNullOrWhiteSpace(metadata.TargetFramework))
            {
                targetFramework = metadata.TargetFramework;
            }

            if (targetFrameworks.Count == 0 && metadata.TargetFrameworks.Count > 0)
            {
                targetFrameworks = metadata.TargetFrameworks;
                if (string.IsNullOrWhiteSpace(targetFramework))
                {
                    targetFramework = targetFrameworks[0];
                }
            }

            if (string.IsNullOrWhiteSpace(langVersion) && !string.IsNullOrWhiteSpace(metadata.LangVersion))
            {
                langVersion = metadata.LangVersion;
            }
        }

        return new DotNetProjectMetadata(targetFramework, targetFrameworks, langVersion);
    }

    private static DotNetProjectMetadata TryReadMsBuildMetadata(string fullPath)
    {
        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists || fileInfo.Length > MaxContentScanBytes)
        {
            return DotNetProjectMetadata.Empty;
        }

        try
        {
            var document = XDocument.Load(fullPath, LoadOptions.None);
            var targetFramework = document.Descendants()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, "TargetFramework", StringComparison.OrdinalIgnoreCase))
                ?.Value
                .Trim() ?? string.Empty;
            var targetFrameworksRaw = document.Descendants()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, "TargetFrameworks", StringComparison.OrdinalIgnoreCase))
                ?.Value
                .Trim() ?? string.Empty;
            var targetFrameworks = targetFrameworksRaw
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
            var langVersion = document.Descendants()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, "LangVersion", StringComparison.OrdinalIgnoreCase))
                ?.Value
                .Trim() ?? string.Empty;

            return new DotNetProjectMetadata(targetFramework, targetFrameworks, langVersion);
        }
        catch
        {
            return DotNetProjectMetadata.Empty;
        }
    }

    private readonly record struct DotNetProjectMetadata(
        string TargetFramework,
        IReadOnlyList<string> TargetFrameworks,
        string LangVersion)
    {
        public static DotNetProjectMetadata Empty => new(string.Empty, Array.Empty<string>(), string.Empty);
    }

    private static IReadOnlyList<string> DetectAvailableTestLibraries(
        IReadOnlyList<AnalyzedRepositoryFile> files,
        ProjectDetection detection)
    {
        if (!string.Equals(detection.BuildTool, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<string>();
        }

        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var projectFile in files.Where(file => file.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            var fileInfo = new FileInfo(projectFile.FullPath);
            if (!fileInfo.Exists || fileInfo.Length > MaxContentScanBytes)
            {
                continue;
            }

            try
            {
                var document = XDocument.Load(projectFile.FullPath, LoadOptions.None);
                foreach (var packageReference in document.Descendants()
                             .Where(element => string.Equals(element.Name.LocalName, "PackageReference", StringComparison.OrdinalIgnoreCase)))
                {
                    var include = packageReference.Attribute("Include")?.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(include))
                    {
                        continue;
                    }

                    if (KnownTestLibraries.TryGetValue(include, out var libraryName))
                    {
                        libraries.Add(libraryName);
                    }
                }
            }
            catch
            {
                // Ignore malformed project files during analysis and continue best-effort detection.
            }
        }

        return libraries
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ExpandRelevantFiles(
        IReadOnlyList<string> relevantFiles,
        IReadOnlyList<CodeSymbolInfo> symbols,
        IReadOnlyList<AnalyzedRepositoryFile> files,
        ProjectDetection detection)
    {
        var expanded = ExpandRelevantFilesWithReferencedTypes(relevantFiles, symbols, files);
        if (!string.Equals(detection.BuildTool, "dotnet", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(detection.Language, "C#", StringComparison.OrdinalIgnoreCase))
        {
            return expanded;
        }

        var index = DotNetDependencyIndexBuilder.Build(files);
        return ExpandRelevantFilesWithDependencyWalk(expanded, files, index);
    }

    private static int ScoreDependencyPath(string relativePath, string typeName)
    {
        var normalizedPath = NormalizeForSearch(relativePath);
        var normalizedTypeName = NormalizeForSearch(typeName);
        var score = 0;

        if (Path.GetFileNameWithoutExtension(relativePath).Contains(typeName, StringComparison.OrdinalIgnoreCase))
        {
            score += 4;
        }

        if (normalizedPath.Contains(normalizedTypeName, StringComparison.OrdinalIgnoreCase))
        {
            score += 2;
        }

        return score;
    }

    private static IReadOnlyList<string> ExpandRelevantFilesWithReferencedTypes(
        IReadOnlyList<string> relevantFiles,
        IReadOnlyList<CodeSymbolInfo> symbols,
        IReadOnlyList<AnalyzedRepositoryFile> files)
    {
        var orderedRelevantFiles = relevantFiles.ToList();
        var relevantFileSet = orderedRelevantFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var referencedTypeNames = symbols
            .Where(symbol => relevantFileSet.Contains(symbol.SourceFile))
            .SelectMany(symbol => symbol.ReferencedTypeNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (referencedTypeNames.Length == 0)
        {
            return orderedRelevantFiles;
        }

        var symbolLookup = symbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol.Name))
            .GroupBy(symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var typeName in referencedTypeNames)
        {
            if (!symbolLookup.TryGetValue(typeName, out var matches))
            {
                continue;
            }

            var bestMatch = matches
                .OrderByDescending(symbol => relevantFileSet.Contains(symbol.SourceFile) ? 1 : 0)
                .ThenByDescending(symbol => ScoreDependencyPath(symbol.SourceFile, typeName))
                .ThenBy(symbol => symbol.SourceFile, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (bestMatch is null || relevantFileSet.Contains(bestMatch.SourceFile))
            {
                continue;
            }

            var file = files.FirstOrDefault(candidate => string.Equals(candidate.RelativePath, bestMatch.SourceFile, StringComparison.OrdinalIgnoreCase));
            if (file is null || !SourceExtensions.Contains(Path.GetExtension(file.FullPath)))
            {
                continue;
            }

            relevantFileSet.Add(bestMatch.SourceFile);
            orderedRelevantFiles.Add(bestMatch.SourceFile);
        }

        return orderedRelevantFiles;
    }

    private static IReadOnlyList<string> ExpandRelevantFilesWithDependencyWalk(
        IReadOnlyList<string> seedFiles,
        IReadOnlyList<AnalyzedRepositoryFile> files,
        DotNetDependencyIndex index)
    {
        var orderedFiles = seedFiles.ToList();
        var includedFiles = orderedFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scores = orderedFiles.ToDictionary(path => path, _ => 100, StringComparer.OrdinalIgnoreCase);
        var visitedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string File, int Depth)>(orderedFiles.Select(file => (file, 0)));

        while (queue.Count > 0)
        {
            var (currentFile, depth) = queue.Dequeue();
            if (depth >= MaxDependencyWalkDepth ||
                !index.FileDependencies.TryGetValue(currentFile, out var tokens))
            {
                continue;
            }

            foreach (var token in tokens)
            {
                if (!visitedTypes.Add($"{token.Kind}:{token.TypeName}:{currentFile}:{depth}"))
                {
                    continue;
                }

                foreach (var resolved in ResolveDependencyFiles(token, currentFile, index))
                {
                    if (!IsResolvableSourceFile(resolved, files))
                    {
                        continue;
                    }

                    var weight = GetDependencyWeight(token.Kind) + ScoreDependencyProximity(currentFile, resolved, token.TypeName);
                    if (scores.TryGetValue(resolved, out var currentScore))
                    {
                        scores[resolved] = Math.Max(currentScore, weight);
                    }
                    else
                    {
                        scores[resolved] = weight;
                    }

                    if (includedFiles.Add(resolved))
                    {
                        orderedFiles.Add(resolved);
                        queue.Enqueue((resolved, depth + 1));
                    }
                }

                if (token.Kind == DependencyTokenKind.ConstructedType)
                {
                    ApplyFamilyCompleteness(token.TypeName, currentFile, index, files, includedFiles, orderedFiles, scores, queue, depth);
                }
            }
        }

        return orderedFiles
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path => scores.TryGetValue(path, out var score) ? score : 0)
            .ThenBy(path => seedFiles.Contains(path, StringComparer.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void ApplyFamilyCompleteness(
        string constructedTypeName,
        string currentFile,
        DotNetDependencyIndex index,
        IReadOnlyList<AnalyzedRepositoryFile> files,
        ISet<string> includedFiles,
        ICollection<string> orderedFiles,
        IDictionary<string, int> scores,
        Queue<(string File, int Depth)> queue,
        int depth)
    {
        if (!index.TypeToBaseType.TryGetValue(constructedTypeName, out var baseType))
        {
            return;
        }

        foreach (var baseFile in ResolveTypeToFiles(baseType, index))
        {
            AddResolvedFile(baseFile, 95, files, includedFiles, orderedFiles, scores, queue, depth);
        }

        if (!index.FileDependencies.TryGetValue(currentFile, out var currentFileDependencies))
        {
            return;
        }

        var siblingConstructedTypes = currentFileDependencies
            .Where(token => token.Kind == DependencyTokenKind.ConstructedType)
            .Select(token => token.TypeName)
            .Where(typeName => index.TypeToBaseType.TryGetValue(typeName, out var siblingBaseType) &&
                               string.Equals(siblingBaseType, baseType, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var siblingType in siblingConstructedTypes)
        {
            foreach (var siblingFile in ResolveTypeToFiles(siblingType, index))
            {
                AddResolvedFile(siblingFile, 90, files, includedFiles, orderedFiles, scores, queue, depth);
            }
        }
    }

    private static IEnumerable<string> ResolveDependencyFiles(
        DependencyToken token,
        string currentFile,
        DotNetDependencyIndex index)
    {
        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (token.Kind == DependencyTokenKind.InjectedType &&
            index.InterfaceToRegisteredImplementationType.TryGetValue(token.TypeName, out var registeredImplementationType))
        {
            foreach (var file in ResolveTypeToFiles(registeredImplementationType, index))
            {
                resolved.Add(file);
            }
        }

        if (token.Kind == DependencyTokenKind.InjectedType &&
            index.InterfaceToImplementationFiles.TryGetValue(token.TypeName, out var implementationFiles))
        {
            foreach (var file in implementationFiles
                         .OrderByDescending(file => ScoreDependencyProximity(currentFile, file, token.TypeName))
                         .ThenBy(file => file, StringComparer.OrdinalIgnoreCase)
                         .Take(2))
            {
                resolved.Add(file);
            }
        }

        foreach (var file in ResolveTypeToFiles(token.TypeName, index))
        {
            resolved.Add(file);
        }

        if (token.Kind == DependencyTokenKind.InjectedType && token.TypeName.StartsWith("I", StringComparison.Ordinal))
        {
            var fallbackTypeName = token.TypeName[1..];
            foreach (var file in ResolveTypeToFiles(fallbackTypeName, index))
            {
                resolved.Add(file);
            }
        }

        if ((token.Kind == DependencyTokenKind.ConstructedType ||
             token.Kind == DependencyTokenKind.ParameterType ||
             token.Kind == DependencyTokenKind.ReturnType) &&
            index.TypeToBaseType.TryGetValue(token.TypeName, out var baseType))
        {
            foreach (var file in ResolveTypeToFiles(baseType, index))
            {
                resolved.Add(file);
            }
        }

        return resolved;
    }

    private static IEnumerable<string> ResolveTypeToFiles(string typeName, DotNetDependencyIndex index)
    {
        return index.TypeToFiles.TryGetValue(typeName, out var files)
            ? files
            : Array.Empty<string>();
    }

    private static bool IsResolvableSourceFile(string relativePath, IReadOnlyList<AnalyzedRepositoryFile> files)
    {
        var file = files.FirstOrDefault(candidate => string.Equals(candidate.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        return file is not null && SourceExtensions.Contains(Path.GetExtension(file.FullPath));
    }

    private static void AddResolvedFile(
        string resolvedFile,
        int score,
        IReadOnlyList<AnalyzedRepositoryFile> files,
        ISet<string> includedFiles,
        ICollection<string> orderedFiles,
        IDictionary<string, int> scores,
        Queue<(string File, int Depth)> queue,
        int depth)
    {
        if (!IsResolvableSourceFile(resolvedFile, files))
        {
            return;
        }

        if (scores.TryGetValue(resolvedFile, out var current))
        {
            scores[resolvedFile] = Math.Max(current, score);
        }
        else
        {
            scores[resolvedFile] = score;
        }

        if (includedFiles.Add(resolvedFile))
        {
            orderedFiles.Add(resolvedFile);
            queue.Enqueue((resolvedFile, depth + 1));
        }
    }

    private static int GetDependencyWeight(DependencyTokenKind kind)
    {
        return kind switch
        {
            DependencyTokenKind.ConstructedType => 90,
            DependencyTokenKind.InjectedType => 85,
            DependencyTokenKind.BaseType => 80,
            DependencyTokenKind.ParameterType => 72,
            DependencyTokenKind.ReturnType => 70,
            DependencyTokenKind.ImplementedInterface => 60,
            _ => 50
        };
    }

    private static int ScoreDependencyProximity(string sourceFile, string candidateFile, string typeName)
    {
        var score = ScoreDependencyPath(candidateFile, typeName);
        var sourceDirectory = Path.GetDirectoryName(sourceFile.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
        var candidateDirectory = Path.GetDirectoryName(candidateFile.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;

        if (string.Equals(sourceDirectory, candidateDirectory, StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }
        else if (!string.IsNullOrWhiteSpace(sourceDirectory) &&
                 candidateDirectory.StartsWith(sourceDirectory, StringComparison.OrdinalIgnoreCase))
        {
            score += 3;
        }

        return score;
    }

    private static ProjectDetection DetectProjectType(
        string repositoryPath,
        IReadOnlyList<AnalyzedRepositoryFile> files)
    {
        var projectFiles = FindProjectFiles(files);

        if (projectFiles.Any(file => HasFileName(file, ".sln") || HasFileName(file, ".slnx") || HasFileName(file, ".csproj")))
        {
            var framework = projectFiles.Any(file => IsAspNetProject(file.FullPath))
                ? "ASP.NET Core"
                : ".NET";
            var testFramework = DetectDotNetTestFramework(projectFiles);

            return new ProjectDetection
            {
                Language = "C#",
                Framework = framework,
                BuildTool = "dotnet",
                TestCommand = "dotnet test",
                TestFramework = testFramework,
                DetectedProjectFiles = projectFiles.Select(file => file.RelativePath).ToArray()
            };
        }

        var pomFile = projectFiles.FirstOrDefault(file => HasFileName(file, "pom.xml"));
        if (pomFile is not null)
        {
            var framework = FileContains(pomFile.FullPath, "spring-boot")
                ? "Spring Boot"
                : "Java";

            return new ProjectDetection
            {
                Language = "Java",
                Framework = framework,
                BuildTool = "Maven",
                TestCommand = "mvn test",
                DetectedProjectFiles = projectFiles.Select(file => file.RelativePath).ToArray()
            };
        }

        var gradleFile = projectFiles.FirstOrDefault(file =>
            HasFileName(file, "build.gradle") || HasFileName(file, "build.gradle.kts"));
        if (gradleFile is not null)
        {
            var framework = FileContains(gradleFile.FullPath, "spring-boot")
                ? "Spring Boot"
                : "Java/Kotlin";
            var hasWrapper = File.Exists(Path.Combine(repositoryPath, "gradlew")) ||
                             File.Exists(Path.Combine(repositoryPath, "gradlew.bat"));

            return new ProjectDetection
            {
                Language = "Java/Kotlin",
                Framework = framework,
                BuildTool = "Gradle",
                TestCommand = hasWrapper ? "./gradlew test" : "gradle test",
                DetectedProjectFiles = projectFiles.Select(file => file.RelativePath).ToArray()
            };
        }

        if (projectFiles.Any(file => HasFileName(file, "package.json")))
        {
            return new ProjectDetection
            {
                Language = "JavaScript/TypeScript",
                Framework = "JavaScript/TypeScript",
                BuildTool = "npm",
                TestCommand = "npm test",
                DetectedProjectFiles = projectFiles.Select(file => file.RelativePath).ToArray()
            };
        }

        if (projectFiles.Any(file => HasFileName(file, "go.mod")))
        {
            return new ProjectDetection
            {
                Language = "Go",
                Framework = "Go",
                BuildTool = "go",
                TestCommand = "go test ./...",
                DetectedProjectFiles = projectFiles.Select(file => file.RelativePath).ToArray()
            };
        }

        if (projectFiles.Any(file => HasFileName(file, "pyproject.toml")))
        {
            return new ProjectDetection
            {
                Language = "Python",
                Framework = "Python",
                BuildTool = "poetry",
                TestCommand = "pytest",
                DetectedProjectFiles = projectFiles.Select(file => file.RelativePath).ToArray()
            };
        }

        if (projectFiles.Any(file => HasFileName(file, "requirements.txt")))
        {
            return new ProjectDetection
            {
                Language = "Python",
                Framework = "Python",
                BuildTool = "pip",
                TestCommand = "pytest",
                DetectedProjectFiles = projectFiles.Select(file => file.RelativePath).ToArray()
            };
        }

        return new ProjectDetection
        {
            DetectedProjectFiles = projectFiles.Select(file => file.RelativePath).ToArray()
        };
    }

    private static string DetectDotNetTestFramework(IReadOnlyList<AnalyzedRepositoryFile> projectFiles)
    {
        var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["xUnit"] = 0,
            ["NUnit"] = 0,
            ["MSTest"] = 0
        };

        foreach (var projectFile in projectFiles.Where(file => HasFileName(file, ".csproj")))
        {
            var fileInfo = new FileInfo(projectFile.FullPath);
            if (!fileInfo.Exists || fileInfo.Length > MaxContentScanBytes)
            {
                continue;
            }

            var content = File.ReadAllText(projectFile.FullPath);

            if (content.Contains("PackageReference Include=\"xunit\"", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("<Using Include=\"Xunit\"", StringComparison.OrdinalIgnoreCase))
            {
                scores["xUnit"] += 2;
            }

            if (content.Contains("PackageReference Include=\"NUnit\"", StringComparison.OrdinalIgnoreCase))
            {
                scores["NUnit"] += 2;
            }

            if (content.Contains("PackageReference Include=\"MSTest.TestFramework\"", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("PackageReference Include=\"MSTest\"", StringComparison.OrdinalIgnoreCase))
            {
                scores["MSTest"] += 2;
            }
        }

        var best = scores
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .First();

        return best.Value > 0 ? best.Key : "Unknown";
    }

    private static IReadOnlyList<AnalyzedRepositoryFile> FindProjectFiles(IReadOnlyList<AnalyzedRepositoryFile> files)
    {
        return files
            .Where(file =>
                HasFileName(file, ".sln") ||
                HasFileName(file, ".slnx") ||
                HasFileName(file, ".csproj") ||
                HasFileName(file, "build.gradle") ||
                HasFileName(file, "build.gradle.kts") ||
                (ProjectFileNames.Contains(file.FileName) && (!RootOnlyProjectFileNames.Contains(file.FileName) || IsRepositoryRootFile(file.RelativePath))))
            .OrderBy(GetProjectFilePriority)
            .ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsRepositoryRootFile(string relativePath)
    {
        return !relativePath.Contains('/') && !relativePath.Contains('\\');
    }

    private static int GetProjectFilePriority(AnalyzedRepositoryFile file)
    {
        if (file.FileName.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (file.FileName.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (string.Equals(file.FileName, "go.work", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (string.Equals(file.FileName, "settings.gradle", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(file.FileName, "settings.gradle.kts", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (string.Equals(file.FileName, "pnpm-workspace.yaml", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(file.FileName, "turbo.json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(file.FileName, "nx.json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(file.FileName, "tsconfig.json", StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        if (file.FileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        if (string.Equals(file.FileName, "go.mod", StringComparison.OrdinalIgnoreCase))
        {
            return 6;
        }

        if (string.Equals(file.FileName, "build.gradle", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(file.FileName, "build.gradle.kts", StringComparison.OrdinalIgnoreCase))
        {
            return 7;
        }

        return 8;
    }

    private static IReadOnlyList<string> FindExistingTestFiles(IReadOnlyList<AnalyzedRepositoryFile> files)
    {
        return files
            .Where(file => SourceExtensions.Contains(Path.GetExtension(file.FullPath)))
            .Where(file => IsTestFile(file.RelativePath))
            .Select(file => file.RelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxResultFiles)
            .ToArray();
    }

    private static async Task<IReadOnlyList<string>> FindRelevantFilesAsync(
        ParsedTask parsedTask,
        IReadOnlyList<AnalyzedRepositoryFile> files,
        IReadOnlyCollection<string> keywords,
        CancellationToken cancellationToken)
    {
        if (keywords.Count == 0)
        {
            return Array.Empty<string>();
        }

        var scoredFiles = new List<(string RelativePath, int Score)>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!SourceExtensions.Contains(Path.GetExtension(file.FullPath)))
            {
                continue;
            }

            var score = ScorePath(file.RelativePath, keywords);
            if (score == 0)
            {
                score = await ScoreContentAsync(file.FullPath, keywords, cancellationToken);
            }

            if (score > 0)
            {
                scoredFiles.Add((file.RelativePath, score));
            }
        }

        return scoredFiles
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.RelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxResultFiles)
            .ToArray();
    }

    private static async Task<int> ScoreContentAsync(
        string fullPath,
        IReadOnlyCollection<string> keywords,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Length > MaxContentScanBytes)
        {
            return 0;
        }

        var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
        var normalized = NormalizeForSearch(content);

        var score = 0;
        foreach (var keyword in keywords)
        {
            if (normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 1;
            }
        }

        return score;
    }

    private static int ScorePath(string relativePath, IReadOnlyCollection<string> keywords)
    {
        var normalizedPath = NormalizeForSearch(relativePath);
        var fileName = NormalizeForSearch(Path.GetFileName(relativePath));
        var directoryName = NormalizeForSearch(Path.GetDirectoryName(relativePath) ?? string.Empty);

        var score = 0;
        foreach (var keyword in keywords)
        {
            if (fileName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 3;
            }

            if (directoryName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 2;
            }

            if (normalizedPath.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 1;
            }
        }

        return score;
    }

    private static HashSet<string> ExtractKeywords(ParsedTask parsedTask)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var content = string.Join(
            ' ',
            new[] { parsedTask.Requirement }
                .Concat(parsedTask.AcceptanceCriteria ?? Array.Empty<string>()));

        foreach (Match match in Regex.Matches(content, @"\/[A-Za-z0-9_\-\/]+"))
        {
            foreach (var segment in match.Value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                AddKeyword(segment, keywords);
            }
        }

        foreach (Match match in Regex.Matches(content, @"[A-Za-z0-9_]+"))
        {
            AddKeyword(match.Value, keywords);
        }

        return keywords;
    }

    private static void AddKeyword(string value, HashSet<string> keywords)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length < 3 || StopWords.Contains(normalized))
        {
            return;
        }

        keywords.Add(normalized);
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "add", "the", "and", "for", "with", "from", "into", "http", "https",
        "api", "endpoint", "should", "returns", "return", "error", "message",
        "invalid", "existing", "update"
    };

    private static bool IsAspNetProject(string fullPath)
    {
        return FileContains(fullPath, "Microsoft.NET.Sdk.Web") ||
               FileContains(fullPath, "Microsoft.AspNetCore");
    }

    private static bool FileContains(string fullPath, string needle)
    {
        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists || fileInfo.Length > MaxContentScanBytes)
        {
            return false;
        }

        return File.ReadAllText(fullPath).Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasFileName(AnalyzedRepositoryFile file, string fileName)
    {
        return string.Equals(file.FileName, fileName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Path.GetExtension(file.FileName), fileName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTestFile(string relativePath)
    {
        var normalizedPath = relativePath.Replace('\\', '/');
        var pathSegments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var fileName = Path.GetFileNameWithoutExtension(relativePath);

        return pathSegments.Any(segment =>
                   string.Equals(segment, "test", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(segment, "tests", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(segment, "spec", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(segment, "__tests__", StringComparison.OrdinalIgnoreCase)) ||
               fileName.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("Tests", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("Spec", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("Should", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateFiles(string rootPath)
    {
        foreach (var file in Directory.EnumerateFiles(rootPath))
        {
            if (!IsIgnoredFile(file))
            {
                yield return file;
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(rootPath))
        {
            if (IgnoredDirectories.Contains(Path.GetFileName(directory)))
            {
                continue;
            }

            foreach (var file in EnumerateFiles(directory))
            {
                yield return file;
            }
        }
    }

    private static bool IsIgnoredFile(string fullPath)
    {
        var fileName = Path.GetFileName(fullPath);

        return string.Equals(fileName, ".env", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "secrets.json", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "appsettings.Production.json", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "package-lock.json", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "yarn.lock", StringComparison.OrdinalIgnoreCase) ||
               HasExtension(fileName, ".pem") ||
               HasExtension(fileName, ".key") ||
               HasExtension(fileName, ".pfx");
    }

    private static bool HasExtension(string fileName, string extension)
    {
        return string.Equals(Path.GetExtension(fileName), extension, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRelativePath(string rootPath, string fullPath)
    {
        return Path.GetRelativePath(rootPath, fullPath).Replace('\\', '/');
    }

    private static string NormalizeForSearch(string value)
    {
        return value.Replace('\\', '/').ToLowerInvariant();
    }
}
