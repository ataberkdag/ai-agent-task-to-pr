using System.Net;
using System.Text;
using AiAgentChallenge.Domain;
using AiAgentChallenge.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class OpenAiCodeAgentTests
{
    [Fact]
    public async Task GenerateChangesAsync_ParsesValidJsonResponse()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");

        var handler = new FakeHttpMessageHandler("""
            {
              "model": "gpt-4.1-mini",
              "choices": [
                {
                  "message": {
                    "content": "{\"summary\":\"Added validation\",\"changedFiles\":[{\"path\":\"src/users/RegisterService.cs\",\"operation\":\"modify\",\"content\":\"public class RegisterService {}\"}],\"testNotes\":\"Add tests\"}"
                  }
                }
              ],
              "usage": {
                "prompt_tokens": 10,
                "completion_tokens": 5,
                "total_tokens": 15
              }
            }
            """);

        using var httpClient = new HttpClient(handler);
        var agent = new OpenAiCodeAgent(
            httpClient,
            Options.Create(new AiOptions { Provider = "OpenAI", Model = "gpt-4.1-mini" }),
            NullLogger<OpenAiCodeAgent>.Instance);

        var result = await agent.GenerateChangesAsync(CreateContext());

        Assert.Equal("Added validation", result.Summary);
        Assert.Single(result.ChangedFiles);
        Assert.Equal("gpt-4.1-mini", result.Usage!.Model);
        Assert.Equal(15, result.Usage.TotalTokens);
        Assert.Equal("https://api.openai.com/v1/chat/completions", handler.LastRequestUri);
    }

    [Fact]
    public async Task GenerateChangesAsync_ThrowsOnInvalidJsonPayload()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");

        var handler = new FakeHttpMessageHandler("""
            {
              "model": "gpt-4.1-mini",
              "choices": [
                {
                  "message": {
                    "content": "not-json"
                  }
                }
              ]
            }
            """);

        using var httpClient = new HttpClient(handler);
        var agent = new OpenAiCodeAgent(
            httpClient,
            Options.Create(new AiOptions { Provider = "OpenAI", Model = "gpt-4.1-mini" }),
            NullLogger<OpenAiCodeAgent>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.GenerateChangesAsync(CreateContext()));
    }

    [Fact]
    public async Task GenerateChangesAsync_UsesConfiguredOpenAiApiBaseUrl()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");

        var handler = new FakeHttpMessageHandler("""
            {
              "model": "gpt-4.1-mini",
              "choices": [
                {
                  "message": {
                    "content": "{\"summary\":\"Added validation\",\"changedFiles\":[{\"path\":\"src/users/RegisterService.cs\",\"operation\":\"modify\",\"content\":\"public class RegisterService {}\"}],\"testNotes\":\"Add tests\"}"
                  }
                }
              ]
            }
            """);

        using var httpClient = new HttpClient(handler);
        var agent = new OpenAiCodeAgent(
            httpClient,
            Options.Create(new AiOptions
            {
                Provider = "OpenAI",
                Model = "gpt-4.1-mini",
                OpenAiApiBaseUrl = "https://example.test"
            }),
            NullLogger<OpenAiCodeAgent>.Instance);

        await agent.GenerateChangesAsync(CreateContext());

        Assert.Equal("https://example.test/v1/chat/completions", handler.LastRequestUri);
    }

    [Fact]
    public async Task GenerateChangesAsync_TrimsTrailingSlashFromConfiguredOpenAiApiBaseUrl()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");

        var handler = new FakeHttpMessageHandler("""
            {
              "model": "gpt-4.1-mini",
              "choices": [
                {
                  "message": {
                    "content": "{\"summary\":\"Added validation\",\"changedFiles\":[{\"path\":\"src/users/RegisterService.cs\",\"operation\":\"modify\",\"content\":\"public class RegisterService {}\"}],\"testNotes\":\"Add tests\"}"
                  }
                }
              ]
            }
            """);

        using var httpClient = new HttpClient(handler);
        var agent = new OpenAiCodeAgent(
            httpClient,
            Options.Create(new AiOptions
            {
                Provider = "OpenAI",
                Model = "gpt-4.1-mini",
                OpenAiApiBaseUrl = "https://example.test/"
            }),
            NullLogger<OpenAiCodeAgent>.Instance);

        await agent.GenerateChangesAsync(CreateContext());

        Assert.Equal("https://example.test/v1/chat/completions", handler.LastRequestUri);
    }

    [Fact]
    public async Task GenerateFixForTestFailureAsync_ParsesValidJsonResponse()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");

        var handler = new FakeHttpMessageHandler("""
            {
              "model": "gpt-4.1-mini",
              "choices": [
                {
                  "message": {
                    "content": "{\"summary\":\"Fixed failing test\",\"changedFiles\":[{\"path\":\"tests/users/RegisterServiceTests.cs\",\"operation\":\"modify\",\"content\":\"public class RegisterServiceTests {}\"}],\"testNotes\":\"Adjusted failing test\"}"
                  }
                }
              ]
            }
            """);

        using var httpClient = new HttpClient(handler);
        var agent = new OpenAiCodeAgent(
            httpClient,
            Options.Create(new AiOptions { Provider = "OpenAI", Model = "gpt-4.1-mini" }),
            NullLogger<OpenAiCodeAgent>.Instance);

        var result = await agent.GenerateFixForTestFailureAsync(
            CreateContext(),
            new AiCodeChangeResult
            {
                Summary = "Initial change",
                ChangedFiles = new[]
                {
                    new AiChangedFile
                    {
                        Path = "src/users/RegisterService.cs",
                        Operation = "modify",
                        Content = "content"
                    }
                },
                TestNotes = "notes"
            },
            null,
            new TestResult
            {
                Command = "dotnet test",
                Status = TestExecutionStatus.Failed,
                ExitCode = 1,
                Stdout = "stdout",
                Stderr = "stderr",
                AttemptNumber = 1
            });

        Assert.Equal("Fixed failing test", result.Summary);
        Assert.Single(result.ChangedFiles);
    }

    [Fact]
    public async Task RegenerateFormattedChangesAsync_ParsesValidJsonResponse()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");

        var handler = new FakeHttpMessageHandler("""
            {
              "model": "gpt-4.1-mini",
              "choices": [
                {
                  "message": {
                    "content": "{\"summary\":\"Reformatted code\",\"changedFiles\":[{\"path\":\"src/users/RegisterService.cs\",\"operation\":\"modify\",\"content\":\"public class RegisterService\\n{\\n}\"}],\"testNotes\":\"No test changes\"}"
                  }
                }
              ]
            }
            """);

        using var httpClient = new HttpClient(handler);
        var agent = new OpenAiCodeAgent(
            httpClient,
            Options.Create(new AiOptions { Provider = "OpenAI", Model = "gpt-4.1-mini" }),
            NullLogger<OpenAiCodeAgent>.Instance);

        var result = await agent.RegenerateFormattedChangesAsync(
            CreateContext(),
            new AiCodeChangeResult
            {
                Summary = "Initial change",
                ChangedFiles = new[]
                {
                    new AiChangedFile
                    {
                        Path = "src/users/RegisterService.cs",
                        Operation = "modify",
                        Content = "public class RegisterService {}"
                    }
                },
                TestNotes = "notes"
            });

        Assert.Equal("Reformatted code", result.Summary);
        Assert.Single(result.ChangedFiles);
    }

    private static AgentContext CreateContext()
    {
        return new AgentContext
        {
            TaskSummary = "Requirement: Add validation",
            RepositoryAnalysisSummary = "Language: C#",
            SelectedFiles = new[]
            {
                new AgentContextFile
                {
                    Path = "src/users/RegisterService.cs",
                    Content = "public class RegisterService {}"
                }
            }
        };
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public FakeHttpMessageHandler(string responseBody)
        {
            _responseBody = responseBody;
        }

        public string LastRequestUri { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.ToString() ?? string.Empty;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }
}
