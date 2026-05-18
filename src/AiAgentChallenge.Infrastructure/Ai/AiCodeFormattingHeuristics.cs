namespace AiAgentChallenge.Infrastructure.Ai;

internal static class AiCodeFormattingHeuristics
{
    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".java", ".kt", ".ts", ".js", ".py", ".go", ".rb", ".php"
    };

    private const int MinimumCollapsedContentLength = 120;
    private const string CollapsedSourceErrorPrefix = "AI produced suspiciously collapsed source content for file ";

    public static bool IsLikelyCollapsedSource(string path, string content)
    {
        var extension = Path.GetExtension(path);
        if (!SourceExtensions.Contains(extension))
        {
            return false;
        }

        if (content.Length < MinimumCollapsedContentLength)
        {
            return false;
        }

        var newlineCount = content.Count(character => character == '\n');
        if (newlineCount > 1)
        {
            return false;
        }

        var lower = content.ToLowerInvariant();
        var signalCount = 0;

        if (content.Contains('{'))
        {
            signalCount++;
        }

        if (content.Contains('}'))
        {
            signalCount++;
        }

        if (content.Contains(';'))
        {
            signalCount++;
        }

        if (content.Contains("\\n", StringComparison.Ordinal))
        {
            signalCount++;
        }

        if (lower.Contains("class ", StringComparison.Ordinal) ||
            lower.Contains("namespace ", StringComparison.Ordinal) ||
            lower.Contains("public ", StringComparison.Ordinal) ||
            lower.Contains("function ", StringComparison.Ordinal) ||
            lower.Contains("def ", StringComparison.Ordinal) ||
            lower.Contains("import ", StringComparison.Ordinal))
        {
            signalCount++;
        }

        return signalCount >= 3;
    }

    public static string BuildCollapsedSourceError(string path)
    {
        return $"{CollapsedSourceErrorPrefix}'{path}'. Ask the AI to return properly formatted multi-line source.";
    }

    public static bool ContainsCollapsedSourceError(IReadOnlyList<string> errors)
    {
        return errors.Any(error => error.StartsWith(CollapsedSourceErrorPrefix, StringComparison.Ordinal));
    }
}
