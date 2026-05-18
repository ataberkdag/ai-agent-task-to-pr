namespace AiAgentChallenge.Application.Abstractions;

public interface ISecretRedactor
{
    string Redact(string content);
}
