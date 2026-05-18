using System.Text.RegularExpressions;
using AiAgentChallenge.Application.Abstractions;

namespace AiAgentChallenge.Infrastructure.Ai;

public sealed class RegexBasedSecretRedactor : ISecretRedactor
{
    private static readonly Regex[] NamedSecretPatterns =
    {
        new(@"(?im)(\b(?:api[_-]?key|apikey|secret|token|password)\b\s*[:=]\s*[""']?)([^""'\r\n;]+)([""']?)", RegexOptions.Compiled),
        new(@"(?im)(\b(?:connection\s*string|accountkey|sharedaccesskey|pwd|user\s*id|uid)\b\s*[:=]\s*[""']?)([^""'\r\n;]+)([""']?)", RegexOptions.Compiled)
    };

    private static readonly Regex[] TokenPatterns =
    {
        new(@"ghp_[A-Za-z0-9]{20,}", RegexOptions.Compiled),
        new(@"sk-[A-Za-z0-9\-_]{10,}", RegexOptions.Compiled)
    };

    public string Redact(string content)
    {
        var redacted = content ?? string.Empty;

        foreach (var pattern in NamedSecretPatterns)
        {
            redacted = pattern.Replace(redacted, "$1[REDACTED]$3");
        }

        foreach (var pattern in TokenPatterns)
        {
            redacted = pattern.Replace(redacted, "[REDACTED]");
        }

        return redacted;
    }
}
