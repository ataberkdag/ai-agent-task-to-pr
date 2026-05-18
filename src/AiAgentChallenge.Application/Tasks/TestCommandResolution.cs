namespace AiAgentChallenge.Application.Tasks;

public sealed class TestCommandResolution
{
    private TestCommandResolution(
        bool isSupported,
        string normalizedCommand,
        string executable,
        IReadOnlyList<string> arguments,
        string reason)
    {
        IsSupported = isSupported;
        NormalizedCommand = normalizedCommand;
        Executable = executable;
        Arguments = arguments;
        Reason = reason;
    }

    public bool IsSupported { get; }

    public string NormalizedCommand { get; }

    public string Executable { get; }

    public IReadOnlyList<string> Arguments { get; }

    public string Reason { get; }

    public static TestCommandResolution Supported(
        string normalizedCommand,
        string executable,
        IReadOnlyList<string> arguments)
    {
        return new TestCommandResolution(true, normalizedCommand, executable, arguments, string.Empty);
    }

    public static TestCommandResolution Unsupported(string normalizedCommand, string reason)
    {
        return new TestCommandResolution(false, normalizedCommand, string.Empty, Array.Empty<string>(), reason);
    }
}
