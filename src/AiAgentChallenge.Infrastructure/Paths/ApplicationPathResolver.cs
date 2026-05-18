namespace AiAgentChallenge.Infrastructure.Paths;

public static class ApplicationPathResolver
{
    public static string GetApplicationBaseDirectory()
    {
        return AppContext.BaseDirectory;
    }

    public static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AiAgentChallenge.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    public static string ResolveAgainstApplicationBase(string? path, string defaultRelativePath)
    {
        var candidate = string.IsNullOrWhiteSpace(path)
            ? defaultRelativePath
            : path;

        if (!Path.IsPathRooted(candidate))
        {
            candidate = Path.Combine(GetApplicationBaseDirectory(), candidate);
        }

        return Path.GetFullPath(candidate);
    }
}
