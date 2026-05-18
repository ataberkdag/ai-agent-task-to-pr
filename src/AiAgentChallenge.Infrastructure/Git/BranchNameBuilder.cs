using System.Text;
using AiAgentChallenge.Application.Abstractions;

namespace AiAgentChallenge.Infrastructure.Git;

public sealed class BranchNameBuilder : IBranchNameBuilder
{
    private const int MaxBranchLength = 100;

    public string Build(string taskId, string title)
    {
        var safeTaskId = Slugify(taskId, preserveCase: true);
        var safeTitle = Slugify(title, preserveCase: false);
        var branch = $"ai-agent/{safeTaskId}-{safeTitle}".TrimEnd('-');

        return branch.Length <= MaxBranchLength
            ? branch
            : branch[..MaxBranchLength].TrimEnd('-');
    }

    private static string Slugify(string value, bool preserveCase)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "task";
        }

        var builder = new StringBuilder();
        var previousWasDash = false;

        foreach (var character in value.Trim())
        {
            var normalized = preserveCase ? character : char.ToLowerInvariant(character);

            if (char.IsLetterOrDigit(normalized))
            {
                builder.Append(normalized);
                previousWasDash = false;
            }
            else if (!previousWasDash)
            {
                builder.Append('-');
                previousWasDash = true;
            }
        }

        var result = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "task" : result;
    }
}
