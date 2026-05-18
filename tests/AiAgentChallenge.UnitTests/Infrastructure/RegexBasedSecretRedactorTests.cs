using AiAgentChallenge.Infrastructure.Ai;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class RegexBasedSecretRedactorTests
{
    [Fact]
    public void Redact_RedactsTokenPasswordAndApiKeyValues()
    {
        var redactor = new RegexBasedSecretRedactor();
        var content = """
            api_key=super-secret
            password: hunter2
            token="abc123"
            github=ghp_123456789012345678901234567890
            openai=sk-abcdefghijklmnopqrstuvwxyz
            """;

        var redacted = redactor.Redact(content);

        Assert.DoesNotContain("super-secret", redacted);
        Assert.DoesNotContain("hunter2", redacted);
        Assert.DoesNotContain("abc123", redacted);
        Assert.DoesNotContain("ghp_123456789012345678901234567890", redacted);
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz", redacted);
        Assert.Contains("[REDACTED]", redacted);
    }
}
