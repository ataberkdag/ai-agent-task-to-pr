using System.Text;
using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Domain;
using AiAgentChallenge.Infrastructure.Ai;

namespace AiAgentChallenge.Infrastructure.Files;

public sealed class SafeFileChangeApplier : IFileChangeApplier
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    public async Task<IReadOnlyList<string>> ApplyAsync(
        string repositoryPath,
        IReadOnlyList<AiChangedFile> changes,
        CancellationToken cancellationToken = default)
    {
        var changedFiles = new List<string>();

        foreach (var change in changes)
        {
            if (!RepositoryFileSecurityPolicy.TryResolveSafePath(repositoryPath, change.Path, out var fullPath))
            {
                throw new InvalidOperationException($"Changed file path '{change.Path}' must remain inside the repository.");
            }

            if (RepositoryFileSecurityPolicy.IsSensitiveRelativePath(change.Path))
            {
                throw new InvalidOperationException($"Sensitive file '{change.Path}' cannot be modified.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var normalizedContent = change.Content
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal);
            await File.WriteAllTextAsync(fullPath, normalizedContent, Utf8WithoutBom, cancellationToken);
            changedFiles.Add(RepositoryFileSecurityPolicy.NormalizeRelativePath(change.Path));
        }

        return changedFiles;
    }
}
