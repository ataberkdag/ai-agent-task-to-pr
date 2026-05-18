namespace AiAgentChallenge.Infrastructure.Text;

internal static class ExecutionOutputLogFormatter
{
    public static string BuildFirstLinesSummary(string? value, int maxLines = 3)
    {
        var lines = ReportTextFormatter.ToLines(value);
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(" | ", lines.Take(Math.Max(1, maxLines)));
    }
}
