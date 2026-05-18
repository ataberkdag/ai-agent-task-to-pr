namespace AiAgentChallenge.Infrastructure.Ai;

internal static class RepositoryFileSecurityPolicy
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

    private static readonly HashSet<string> SensitiveFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".env",
        "secrets.json",
        "appsettings.Production.json",
        "id_rsa",
        "id_dsa"
    };

    private static readonly HashSet<string> SensitiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pem",
        ".key",
        ".pfx"
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".bmp",
        ".ico",
        ".pdf",
        ".zip",
        ".gz",
        ".tar",
        ".7z",
        ".dll",
        ".exe",
        ".so",
        ".dylib",
        ".class",
        ".jar"
    };

    public static bool IsIgnoredDirectory(string pathSegment)
    {
        return IgnoredDirectories.Contains(pathSegment);
    }

    public static bool IsSensitiveRelativePath(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);
        return SensitiveFileNames.Contains(fileName) ||
               fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase) ||
               SensitiveExtensions.Contains(Path.GetExtension(fileName));
    }

    public static bool IsBinaryPath(string relativePath)
    {
        return BinaryExtensions.Contains(Path.GetExtension(relativePath));
    }

    public static string NormalizeRelativePath(string path)
    {
        return path.Replace('\\', '/').Trim();
    }

    public static bool TryResolveSafePath(string repositoryPath, string relativePath, out string safeFullPath)
    {
        safeFullPath = string.Empty;

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var normalized = NormalizeRelativePath(relativePath);
        if (Path.IsPathRooted(normalized) || normalized.StartsWith('/'))
        {
            return false;
        }

        var repositoryRoot = Path.GetFullPath(repositoryPath);
        var candidate = Path.GetFullPath(Path.Combine(repositoryRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = repositoryRoot.EndsWith(Path.DirectorySeparatorChar)
            ? repositoryRoot
            : repositoryRoot + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(candidate, repositoryRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        safeFullPath = candidate;
        return true;
    }

    public static bool IsBinaryContent(string fullPath, int probeBytes = 1024)
    {
        if (!File.Exists(fullPath))
        {
            return false;
        }

        using var stream = File.OpenRead(fullPath);
        var buffer = new byte[Math.Min(probeBytes, (int)Math.Min(stream.Length, probeBytes))];
        var bytesRead = stream.Read(buffer, 0, buffer.Length);

        for (var i = 0; i < bytesRead; i++)
        {
            if (buffer[i] == 0)
            {
                return true;
            }
        }

        return false;
    }
}
