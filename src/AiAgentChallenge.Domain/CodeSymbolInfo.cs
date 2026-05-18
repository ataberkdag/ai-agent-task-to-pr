namespace AiAgentChallenge.Domain;

public sealed class CodeSymbolInfo
{
    public string Kind { get; init; } = string.Empty;

    public string SymbolType { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Namespace { get; init; } = string.Empty;

    public string SourceFile { get; init; } = string.Empty;

    public string DisplaySignature { get; init; } = string.Empty;

    public string ReturnType { get; init; } = string.Empty;

    public IReadOnlyList<CodeParameterInfo> Parameters { get; init; } = Array.Empty<CodeParameterInfo>();

    public IReadOnlyList<string> ConstructorDependencies { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RouteAttributes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ReferencedTypeNames { get; init; } = Array.Empty<string>();
}
