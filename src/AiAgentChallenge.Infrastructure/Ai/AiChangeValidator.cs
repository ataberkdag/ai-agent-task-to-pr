using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Domain;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.Infrastructure.Ai;

public sealed class AiChangeValidator : IAiChangeValidator
{
    private static readonly string[] XunitMarkers =
    {
        "[Fact]",
        "[Theory]",
        "[InlineData]",
        "Assert."
    };

    private readonly AiOptions _options;

    public AiChangeValidator(IOptions<AiOptions> options)
    {
        _options = options.Value;
    }

    public AiChangeValidationResult Validate(
        string repositoryPath,
        RepositoryAnalysis repositoryAnalysis,
        AiCodeChangeResult aiCodeChangeResult)
    {
        if (aiCodeChangeResult.ChangedFiles is null || aiCodeChangeResult.ChangedFiles.Count == 0)
        {
            return AiChangeValidationResult.Failure("AI response must include at least one changed file.");
        }

        if (aiCodeChangeResult.ChangedFiles.Count > _options.MaxChangedFiles)
        {
            return AiChangeValidationResult.Failure($"AI attempted to change too many files. Maximum allowed is {_options.MaxChangedFiles}.");
        }

        var warnings = new List<AiChangeWarning>();
        var validatedChanges = new List<AiChangedFile>();
        var normalizedChangedFiles = aiCodeChangeResult.ChangedFiles
            .Select(file => (Original: file, Path: RepositoryFileSecurityPolicy.NormalizeRelativePath(file.Path)))
            .ToArray();
        var allowedPaths = repositoryAnalysis.RelevantFiles
            .Concat(repositoryAnalysis.ExistingTestFiles)
            .Select(RepositoryFileSecurityPolicy.NormalizeRelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in normalizedChangedFiles)
        {
            var changedFile = candidate.Original;
            var normalizedPath = candidate.Path;

            if (!RepositoryFileSecurityPolicy.TryResolveSafePath(repositoryPath, normalizedPath, out _))
            {
                return AiChangeValidationResult.Failure($"Changed file path '{changedFile.Path}' must be a safe relative path inside the repository.");
            }

            if (RepositoryFileSecurityPolicy.IsSensitiveRelativePath(normalizedPath))
            {
                return AiChangeValidationResult.Failure($"Sensitive file '{normalizedPath}' cannot be created or modified by AI.");
            }

            if (RepositoryFileSecurityPolicy.IsBinaryPath(normalizedPath))
            {
                return AiChangeValidationResult.Failure($"Binary file '{normalizedPath}' cannot be created or modified by AI.");
            }

            if (!string.Equals(changedFile.Operation, "create", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(changedFile.Operation, "modify", StringComparison.OrdinalIgnoreCase))
            {
                return AiChangeValidationResult.Failure($"Operation '{changedFile.Operation}' is invalid for file '{normalizedPath}'.");
            }

            if (string.IsNullOrWhiteSpace(changedFile.Content))
            {
                return AiChangeValidationResult.Failure($"Content for file '{normalizedPath}' cannot be empty.");
            }

            if (AiCodeFormattingHeuristics.IsLikelyCollapsedSource(normalizedPath, changedFile.Content))
            {
                return AiChangeValidationResult.Failure(AiCodeFormattingHeuristics.BuildCollapsedSourceError(normalizedPath));
            }

            if (RequiresExplicitXunitResolution(repositoryAnalysis, normalizedPath, changedFile.Content) &&
                !HasExplicitXunitUsing(changedFile.Content))
            {
                return AiChangeValidationResult.Failure(
                    $"Generated xUnit test file '{normalizedPath}' must include explicit 'using Xunit;' or 'global using Xunit;' in the same file.");
            }

            if (!allowedPaths.Contains(normalizedPath))
            {
                warnings.Add(new AiChangeWarning
                {
                    Path = normalizedPath,
                    Message = $"AI proposed a change outside the analyzed relevant/test file set: {normalizedPath}"
                });
            }

            validatedChanges.Add(new AiChangedFile
            {
                Path = normalizedPath,
                Operation = changedFile.Operation.ToLowerInvariant(),
                Content = changedFile.Content
            });
        }

        return AiChangeValidationResult.Success(validatedChanges, warnings);
    }

    private static bool RequiresExplicitXunitResolution(RepositoryAnalysis repositoryAnalysis, string normalizedPath, string content)
    {
        if (!string.Equals(repositoryAnalysis.TestFramework, "xUnit", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(Path.GetExtension(normalizedPath), ".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsLikelyTestFile(normalizedPath))
        {
            return false;
        }

        return XunitMarkers.Any(marker => content.Contains(marker, StringComparison.Ordinal));
    }

    private static bool HasExplicitXunitUsing(string content)
    {
        return content.Contains("using Xunit;", StringComparison.Ordinal) ||
               content.Contains("global using Xunit;", StringComparison.Ordinal);
    }

    private static bool IsLikelyTestFile(string normalizedPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(normalizedPath);
        return normalizedPath.Contains("/tests/", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Contains("/test/", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("Tests", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("Spec", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("Should", StringComparison.OrdinalIgnoreCase);
    }
}
