namespace AiAgentChallenge.Infrastructure.Text;

internal static class ReportTextFormatter
{
    public static IReadOnlyList<string> ToLines(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return Array.Empty<string>();
        }

        var normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var parts = normalized.Split('\n');
        var lastIndex = parts.Length - 1;
        while (lastIndex >= 0 && parts[lastIndex].Length == 0)
        {
            lastIndex--;
        }

        if (lastIndex < 0)
        {
            return Array.Empty<string>();
        }

        var lines = new string[lastIndex + 1];
        Array.Copy(parts, lines, lastIndex + 1);
        return lines;
    }
}
