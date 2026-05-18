using System.Text;
using System.Text.RegularExpressions;
using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Domain;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.Infrastructure.Ai;

public sealed class AgentContextBuilder : IAgentContextBuilder
{
    private readonly AiOptions _options;
    private readonly ISecretRedactor _secretRedactor;

    public AgentContextBuilder(IOptions<AiOptions> options, ISecretRedactor secretRedactor)
    {
        _options = options.Value;
        _secretRedactor = secretRedactor;
    }

    public async Task<AgentContext> BuildAsync(
        string repositoryPath,
        ParsedTask parsedTask,
        RepositoryAnalysis repositoryAnalysis,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(repositoryPath))
        {
            throw new DirectoryNotFoundException($"Repository path '{repositoryPath}' does not exist.");
        }

        var mandatoryPaths = repositoryAnalysis.ProjectFiles
            .Where(IsSolutionFile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var optionalPaths = repositoryAnalysis.ProjectFiles
            .Where(path => !IsSolutionFile(path))
            .Concat(repositoryAnalysis.RelevantFiles)
            .Concat(repositoryAnalysis.ExistingTestFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, _options.MaxContextFiles))
            .ToArray();
        var selectedPaths = mandatoryPaths
            .Concat(optionalPaths)
            .ToArray();

        var files = new List<AgentContextFile>();

        foreach (var relativePath in selectedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var normalized = RepositoryFileSecurityPolicy.NormalizeRelativePath(relativePath);
            if (RepositoryFileSecurityPolicy.IsSensitiveRelativePath(normalized) ||
                RepositoryFileSecurityPolicy.IsBinaryPath(normalized) ||
                ContainsIgnoredDirectory(normalized) ||
                !RepositoryFileSecurityPolicy.TryResolveSafePath(repositoryPath, normalized, out var safeFullPath) ||
                !File.Exists(safeFullPath))
            {
                continue;
            }

            var fileInfo = new FileInfo(safeFullPath);
            if ((!IsSolutionFile(normalized) && fileInfo.Length > _options.MaxFileBytes) ||
                RepositoryFileSecurityPolicy.IsBinaryContent(safeFullPath))
            {
                continue;
            }

            var content = await File.ReadAllTextAsync(safeFullPath, cancellationToken);
            files.Add(new AgentContextFile
            {
                Path = normalized,
                Content = _secretRedactor.Redact(content)
            });
        }

        return new AgentContext
        {
            TaskSummary = BuildTaskSummary(parsedTask),
            Language = repositoryAnalysis.Language,
            Framework = repositoryAnalysis.Framework,
            TestFramework = repositoryAnalysis.TestFramework,
            RepositoryAnalysisSummary = BuildRepositoryAnalysisSummary(repositoryAnalysis, _options.MaxCriticalSignatures),
            SelectedFiles = files.ToArray()
        };
    }

    private static string BuildTaskSummary(ParsedTask parsedTask)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Requirement: {parsedTask.Requirement}");

        if (parsedTask.AcceptanceCriteria.Count > 0)
        {
            builder.AppendLine("Acceptance Criteria:");
            foreach (var criterion in parsedTask.AcceptanceCriteria)
            {
                builder.AppendLine($"- {criterion}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildRepositoryAnalysisSummary(RepositoryAnalysis repositoryAnalysis, int maxCriticalSignatures)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Language: {repositoryAnalysis.Language}");
        builder.AppendLine($"Framework: {repositoryAnalysis.Framework}");
        builder.AppendLine($"Build Tool: {repositoryAnalysis.BuildTool}");
        builder.AppendLine($"Test Command: {repositoryAnalysis.TestCommand}");
        builder.AppendLine($"Detected Test Framework: {repositoryAnalysis.TestFramework}");
        if (repositoryAnalysis.AvailableTestLibraries.Count > 0)
        {
            builder.AppendLine($"Available Test Libraries: {string.Join(", ", repositoryAnalysis.AvailableTestLibraries)}");
        }

        if (!string.IsNullOrWhiteSpace(repositoryAnalysis.TargetFramework))
        {
            builder.AppendLine($"Target Framework: {repositoryAnalysis.TargetFramework}");
        }

        if (repositoryAnalysis.TargetFrameworks.Count > 1)
        {
            builder.AppendLine($"Target Frameworks: {string.Join(", ", repositoryAnalysis.TargetFrameworks)}");
        }

        if (!string.IsNullOrWhiteSpace(repositoryAnalysis.LangVersion))
        {
            builder.AppendLine($"LangVersion: {repositoryAnalysis.LangVersion}");
        }

        if (repositoryAnalysis.RelevantFiles.Count > 0)
        {
            builder.AppendLine("Relevant Files:");
            foreach (var file in repositoryAnalysis.RelevantFiles)
            {
                builder.AppendLine($"- {file}");
            }
        }

        if (repositoryAnalysis.ProjectFiles.Count > 0)
        {
            builder.AppendLine("Project Files:");
            foreach (var file in repositoryAnalysis.ProjectFiles)
            {
                builder.AppendLine($"- {file}");
            }
        }

        if (repositoryAnalysis.ExistingTestFiles.Count > 0)
        {
            builder.AppendLine("Existing Test Files:");
            foreach (var file in repositoryAnalysis.ExistingTestFiles)
            {
                builder.AppendLine($"- {file}");
            }
        }

        if (repositoryAnalysis.ApiEndpoints.Count > 0)
        {
            builder.AppendLine("Detected API Endpoints:");
            foreach (var endpoint in repositoryAnalysis.ApiEndpoints.Take(10))
            {
                builder.AppendLine($"- [{endpoint.Style}] {endpoint.HttpMethod} {endpoint.Route} => {endpoint.HandlerName} ({endpoint.SourceFile})");
            }
        }

        if (repositoryAnalysis.Symbols.Count > 0)
        {
            var criticalSignatures = SelectCriticalSignatures(repositoryAnalysis, Math.Max(1, maxCriticalSignatures));
            if (criticalSignatures.Count > 0)
            {
                builder.AppendLine("Critical Signatures:");
                foreach (var signature in criticalSignatures)
                {
                    builder.AppendLine($"- {signature}");
                }
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static IReadOnlyList<string> SelectCriticalSignatures(RepositoryAnalysis repositoryAnalysis, int maxCriticalSignatures)
    {
        var relevantFileSet = repositoryAnalysis.RelevantFiles
            .Concat(repositoryAnalysis.ExistingTestFiles)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var referencedTypeNames = repositoryAnalysis.Symbols
            .Where(symbol => relevantFileSet.Contains(symbol.SourceFile))
            .SelectMany(symbol => symbol.ReferencedTypeNames)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return repositoryAnalysis.Symbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol.DisplaySignature))
            .OrderByDescending(symbol => GetSignaturePriority(symbol, relevantFileSet, referencedTypeNames))
            .ThenBy(symbol => symbol.SourceFile, StringComparer.OrdinalIgnoreCase)
            .ThenBy(symbol => symbol.DisplaySignature, StringComparer.OrdinalIgnoreCase)
            .Select(symbol => new
            {
                Key = BuildSignatureKey(symbol),
                Display = BuildCanonicalDisplaySignature(symbol)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Display))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First().Display)
            .GroupBy(NormalizeFinalDisplayForDedup, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(maxCriticalSignatures)
            .ToArray();
    }

    private static string BuildSignatureKey(CodeSymbolInfo symbol)
    {
        if (symbol.Parameters.Count == 0 && !string.IsNullOrWhiteSpace(symbol.DisplaySignature))
        {
            return string.Join(
                '|',
                NormalizeToken(symbol.SymbolType),
                NormalizeToken(symbol.Name),
                NormalizeToken(NormalizeDisplaySignature(symbol.DisplaySignature, symbol.Name)),
                NormalizeToken(symbol.ReturnType));
        }

        var parameterTypes = symbol.Parameters
            .OrderBy(parameter => parameter.Ordinal)
            .Select(parameter => NormalizeToken(parameter.Type))
            .ToArray();
        var returnType = string.Equals(symbol.SymbolType, "method", StringComparison.OrdinalIgnoreCase)
            ? NormalizeToken(symbol.ReturnType)
            : string.Empty;

        return string.Join(
            '|',
            NormalizeToken(symbol.SymbolType),
            NormalizeToken(symbol.Name),
            string.Join(',', parameterTypes),
            returnType);
    }

    private static string BuildCanonicalDisplaySignature(CodeSymbolInfo symbol)
    {
        if ((string.Equals(symbol.SymbolType, "constructor", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(symbol.SymbolType, "method", StringComparison.OrdinalIgnoreCase)) &&
            symbol.Parameters.Count > 0 &&
            !string.IsNullOrWhiteSpace(symbol.Name))
        {
            return BuildSignature(symbol.Name, symbol.ReturnType, symbol.Parameters);
        }

        return NormalizeDisplaySignature(symbol.DisplaySignature, symbol.Name);
    }

    private static string BuildSignature(string name, string returnType, IReadOnlyList<CodeParameterInfo> parameters)
    {
        var parameterText = string.Join(", ", parameters
            .OrderBy(parameter => parameter.Ordinal)
            .Select(parameter => $"{parameter.Type} {parameter.Name}"));

        return string.IsNullOrWhiteSpace(returnType)
            ? $"{name}({parameterText})"
            : $"{returnType} {name}({parameterText})";
    }

    private static string NormalizeDisplaySignature(string displaySignature, string symbolName)
    {
        if (string.IsNullOrWhiteSpace(displaySignature))
        {
            return string.Empty;
        }

        var normalized = Regex.Replace(displaySignature, @"\s+", " ").Trim();
        normalized = Regex.Replace(normalized, @"\s*:\s*base\s*\([^)]*\)\s*$", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\(\s+", "(", RegexOptions.None);
        normalized = Regex.Replace(normalized, @"\s+\)", ")", RegexOptions.None);
        normalized = Regex.Replace(normalized, @"\s+,", ",", RegexOptions.None);
        normalized = Regex.Replace(normalized, @",\s+", ", ", RegexOptions.None);

        if (!string.IsNullOrWhiteSpace(symbolName))
        {
            var symbolIndex = normalized.IndexOf(symbolName, StringComparison.Ordinal);
            if (symbolIndex > 0 && normalized[symbolIndex..].Contains('('))
            {
                normalized = normalized[symbolIndex..];
            }
        }

        return normalized.Trim();
    }

    private static string NormalizeToken(string value)
    {
        return Regex.Replace(value ?? string.Empty, @"\s+", string.Empty).Trim();
    }

    private static string NormalizeFinalDisplayForDedup(string display)
    {
        return NormalizeDisplaySignature(display, string.Empty);
    }

    private static int GetSignaturePriority(
        CodeSymbolInfo symbol,
        IReadOnlySet<string> relevantFileSet,
        IReadOnlySet<string> referencedTypeNames)
    {
        var score = relevantFileSet.Contains(symbol.SourceFile) ? 10 : 0;

        if (string.Equals(symbol.SymbolType, "constructor", StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }
        else if (string.Equals(symbol.SymbolType, "method", StringComparison.OrdinalIgnoreCase))
        {
            score += 8;
        }

        if (referencedTypeNames.Contains(symbol.Name))
        {
            score += 15;
        }

        var fileName = Path.GetFileNameWithoutExtension(symbol.SourceFile);
        if (fileName.EndsWith("Controller", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("Service", StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }

        if (fileName.EndsWith("Request", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("Response", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("Dto", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("Command", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("Mapper", StringComparison.OrdinalIgnoreCase))
        {
            score += 3;
        }

        return score;
    }

    private static bool ContainsIgnoredDirectory(string normalizedRelativePath)
    {
        var segments = normalizedRelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Any(RepositoryFileSecurityPolicy.IsIgnoredDirectory);
    }

    private static bool IsSolutionFile(string path)
    {
        return path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);
    }
}
