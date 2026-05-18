using System.Text.Json.Serialization;

namespace AiAgentChallenge.Domain;

public sealed class AiCodeChangeResult
{
    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    [JsonPropertyName("changedFiles")]
    public IReadOnlyList<AiChangedFile> ChangedFiles { get; init; } = Array.Empty<AiChangedFile>();

    [JsonPropertyName("testNotes")]
    public string TestNotes { get; init; } = string.Empty;

    [JsonIgnore]
    public AiUsageInfo? Usage { get; init; }

    [JsonIgnore]
    public IReadOnlyList<AiChangeWarning> Warnings { get; init; } = Array.Empty<AiChangeWarning>();
}
