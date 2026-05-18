using System.Text.RegularExpressions;
using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Application.Tasks;
using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Infrastructure.Parsing;

public sealed class RuleBasedTaskParser : ITaskParser
{
    private const string DefaultBranch = "main";

    public Task<TaskParseResult> ParseAsync(string description, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(description);

        var repository = ExtractSingleLineSection(normalized, "Repository");
        if (!repository.IsSuccess)
        {
            return Task.FromResult(repository.ToParseResult());
        }

        var requirement = ExtractBlockSection(normalized, "Requirement", "Acceptance Criteria");
        if (!requirement.IsSuccess)
        {
            return Task.FromResult(requirement.ToParseResult());
        }

        var branch = ExtractSingleLineSection(normalized, "Branch", required: false);
        if (!branch.IsSuccess)
        {
            return Task.FromResult(branch.ToParseResult());
        }

        var acceptanceCriteria = ExtractAcceptanceCriteria(normalized);
        if (!acceptanceCriteria.IsSuccess)
        {
            return Task.FromResult(acceptanceCriteria.ToParseResult());
        }

        if (!Uri.TryCreate(repository.Value, UriKind.Absolute, out var repositoryUri) ||
            (repositoryUri.Scheme != Uri.UriSchemeHttp && repositoryUri.Scheme != Uri.UriSchemeHttps))
        {
            return Task.FromResult(
                TaskParseResult.Failure(
                    "Description.Repository",
                    "Repository must be a valid absolute http or https URL."));
        }

        var parsedTask = new ParsedTask
        {
            RepositoryUrl = repositoryUri.ToString(),
            BaseBranch = string.IsNullOrWhiteSpace(branch.Value) ? DefaultBranch : branch.Value,
            Requirement = requirement.Value,
            AcceptanceCriteria = acceptanceCriteria.AcceptanceCriteria
        };

        return Task.FromResult(TaskParseResult.Success(parsedTask));
    }

    private static string Normalize(string description)
    {
        return (description ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private static SectionResult ExtractSingleLineSection(string input, string sectionName, bool required = true)
    {
        var matches = Regex.Matches(
            input,
            $@"^[ \t]*{Regex.Escape(sectionName)}[ \t]*:[ \t]*(?<value>.+?)[ \t]*$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        if (matches.Count > 1)
        {
            return SectionResult.Failure(
                $"Description.{sectionName.Replace(" ", string.Empty)}",
                $"{sectionName} section is defined more than once.");
        }

        if (matches.Count == 0)
        {
            if (!required)
            {
                return SectionResult.Success(string.Empty);
            }

            return SectionResult.Failure(
                $"Description.{sectionName.Replace(" ", string.Empty)}",
                $"{sectionName} section is required.");
        }

        var value = matches[0].Groups["value"].Value.Trim();
        if (required && string.IsNullOrWhiteSpace(value))
        {
            return SectionResult.Failure(
                $"Description.{sectionName.Replace(" ", string.Empty)}",
                $"{sectionName} section cannot be empty.");
        }

        return SectionResult.Success(value);
    }

    private static SectionResult ExtractBlockSection(string input, string sectionName, params string[] nextSections)
    {
        var boundaryPattern = nextSections.Length == 0
            ? @"\z"
            : $"(?=^[ \\t]*(?:{string.Join("|", nextSections.Select(Regex.Escape))})[ \\t]*:|\\z)";

        var matches = Regex.Matches(
            input,
            $@"^[ \t]*{Regex.Escape(sectionName)}[ \t]*:[ \t]*\n?(?<value>.*?){boundaryPattern}",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);

        if (matches.Count > 1)
        {
            return SectionResult.Failure(
                $"Description.{sectionName.Replace(" ", string.Empty)}",
                $"{sectionName} section is defined more than once.");
        }

        if (matches.Count == 0)
        {
            return SectionResult.Failure(
                $"Description.{sectionName.Replace(" ", string.Empty)}",
                $"{sectionName} section is required.");
        }

        var value = matches[0].Groups["value"].Value.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return SectionResult.Failure(
                $"Description.{sectionName.Replace(" ", string.Empty)}",
                $"{sectionName} section cannot be empty.");
        }

        return SectionResult.Success(value);
    }

    private static AcceptanceCriteriaResult ExtractAcceptanceCriteria(string input)
    {
        var matches = Regex.Matches(
            input,
            @"^[ \t]*Acceptance Criteria[ \t]*:[ \t]*\n?(?<value>.*?)(?=\z)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);

        if (matches.Count > 1)
        {
            return AcceptanceCriteriaResult.Failure(
                "Description.AcceptanceCriteria",
                "Acceptance Criteria section is defined more than once.");
        }

        if (matches.Count == 0)
        {
            return AcceptanceCriteriaResult.Success(Array.Empty<string>());
        }

        var lines = matches[0].Groups["value"].Value
            .Split('\n', StringSplitOptions.TrimEntries);

        var items = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parsedLine = Regex.Replace(line, @"^(?:[-*]|\d+\.)\s*", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(parsedLine))
            {
                continue;
            }

            items.Add(parsedLine);
        }

        return AcceptanceCriteriaResult.Success(items);
    }

    private sealed record SectionResult(bool IsSuccess, string Value, string Key, string Message)
    {
        public static SectionResult Success(string value) => new(true, value, string.Empty, string.Empty);

        public static SectionResult Failure(string key, string message) => new(false, string.Empty, key, message);

        public TaskParseResult ToParseResult() => TaskParseResult.Failure(Key, Message);
    }

    private sealed record AcceptanceCriteriaResult(
        bool IsSuccess,
        IReadOnlyList<string> AcceptanceCriteria,
        string Key,
        string Message)
    {
        public static AcceptanceCriteriaResult Success(IReadOnlyList<string> acceptanceCriteria) =>
            new(true, acceptanceCriteria, string.Empty, string.Empty);

        public static AcceptanceCriteriaResult Failure(string key, string message) =>
            new(false, Array.Empty<string>(), key, message);

        public TaskParseResult ToParseResult() => TaskParseResult.Failure(Key, Message);
    }
}
