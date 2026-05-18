using AiAgentChallenge.Domain;
using AiAgentChallenge.Infrastructure.Files;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class SafeFileChangeApplierTests
{
    [Fact]
    public async Task ApplyAsync_WritesFileInsideRepository()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), "file-applier-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var applier = new SafeFileChangeApplier();

            var changedFiles = await applier.ApplyAsync(
                repositoryPath,
                new[]
                {
                    new AiChangedFile
                    {
                        Path = "src/users/RegisterService.cs",
                        Operation = "modify",
                        Content = "public class RegisterService {}"
                    }
                });

            Assert.Contains("src/users/RegisterService.cs", changedFiles);
            Assert.True(File.Exists(Path.Combine(repositoryPath, "src", "users", "RegisterService.cs")));
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
            {
                Directory.Delete(repositoryPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ApplyAsync_RejectsRepositoryEscape()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), "file-applier-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var applier = new SafeFileChangeApplier();

            await Assert.ThrowsAsync<InvalidOperationException>(() => applier.ApplyAsync(
                repositoryPath,
                new[]
                {
                    new AiChangedFile
                    {
                        Path = "../outside.cs",
                        Operation = "modify",
                        Content = "test"
                    }
                }));
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
            {
                Directory.Delete(repositoryPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ApplyAsync_NormalizesLineEndings()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), "file-applier-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var applier = new SafeFileChangeApplier();

            await applier.ApplyAsync(
                repositoryPath,
                new[]
                {
                    new AiChangedFile
                    {
                        Path = "src/users/RegisterService.cs",
                        Operation = "modify",
                        Content = "line1\r\nline2\rline3\nline4"
                    }
                });

            var content = await File.ReadAllTextAsync(Path.Combine(repositoryPath, "src", "users", "RegisterService.cs"));
            Assert.Equal("line1\nline2\nline3\nline4", content);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
            {
                Directory.Delete(repositoryPath, recursive: true);
            }
        }
    }
}
