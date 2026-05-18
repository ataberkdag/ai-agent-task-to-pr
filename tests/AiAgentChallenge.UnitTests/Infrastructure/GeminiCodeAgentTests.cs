using System.Net;
using System.Text;
using AiAgentChallenge.Domain;
using AiAgentChallenge.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.UnitTests.Infrastructure;

public sealed class GeminiCodeAgentTests
{
    [Fact]
    public async Task GenerateChangesAsync_ParsesValidJsonResponse()
    {
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", "gemini-test-key");

        var handler = new FakeHttpMessageHandler("""
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      {
                        "text": "{\"summary\":\"Added validation\",\"changedFiles\":[{\"path\":\"src/users/RegisterService.cs\",\"operation\":\"modify\",\"content\":\"public class RegisterService {}\"}],\"testNotes\":\"Add tests\"}"
                      }
                    ]
                  }
                }
              ],
              "usageMetadata": {
                "promptTokenCount": 11,
                "candidatesTokenCount": 7,
                "totalTokenCount": 18
              }
            }
            """);

        using var httpClient = new HttpClient(handler);
        var agent = new GeminiCodeAgent(
            httpClient,
            Options.Create(new AiOptions { Provider = "Gemini", Model = "gemini-2.5-flash" }),
            NullLogger<GeminiCodeAgent>.Instance);

        var result = await agent.GenerateChangesAsync(CreateContext());

        Assert.Equal("Added validation", result.Summary);
        Assert.Single(result.ChangedFiles);
        Assert.Equal("gemini-2.5-flash", result.Usage!.Model);
        Assert.Equal(11, result.Usage.InputTokens);
        Assert.Equal(7, result.Usage.OutputTokens);
        Assert.Equal(18, result.Usage.TotalTokens);
    }

    [Fact]
    public async Task GenerateChangesAsync_ThrowsOnInvalidJsonPayload()
    {
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", "gemini-test-key");

        var handler = new FakeHttpMessageHandler("""
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      {
                        "text": "not-json"
                      }
                    ]
                  }
                }
              ]
            }
            """);

        using var httpClient = new HttpClient(handler);
        var agent = new GeminiCodeAgent(
            httpClient,
            Options.Create(new AiOptions { Provider = "Gemini", Model = "gemini-2.5-flash" }),
            NullLogger<GeminiCodeAgent>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.GenerateChangesAsync(CreateContext()));
    }

    [Fact]
    public async Task GenerateChangesAsync_ThrowsWhenApiKeyIsMissing()
    {
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", null);

        using var httpClient = new HttpClient(new FakeHttpMessageHandler("{}"));
        var agent = new GeminiCodeAgent(
            httpClient,
            Options.Create(new AiOptions { Provider = "Gemini", Model = "gemini-2.5-flash" }),
            NullLogger<GeminiCodeAgent>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => agent.GenerateChangesAsync(CreateContext()));

        Assert.Equal("GEMINI_API_KEY environment variable is not configured.", exception.Message);
    }

    [Fact]
    public async Task GenerateFixForTestFailureAsync_ParsesValidJsonResponse()
    {
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", "gemini-test-key");

        var handler = new FakeHttpMessageHandler("""
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      {
                        "text": "{\"summary\":\"Fixed failing test\",\"changedFiles\":[{\"path\":\"tests/users/RegisterServiceTests.cs\",\"operation\":\"modify\",\"content\":\"public class RegisterServiceTests {}\"}],\"testNotes\":\"Adjusted failing test\"}"
                      }
                    ]
                  }
                }
              ]
            }
            """);

        using var httpClient = new HttpClient(handler);
        var agent = new GeminiCodeAgent(
            httpClient,
            Options.Create(new AiOptions { Provider = "Gemini", Model = "gemini-2.5-flash" }),
            NullLogger<GeminiCodeAgent>.Instance);

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
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", "gemini-test-key");

        var handler = new FakeHttpMessageHandler("""
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      {
                        "text": "{\"summary\":\"Reformatted code\",\"changedFiles\":[{\"path\":\"src/users/RegisterService.cs\",\"operation\":\"modify\",\"content\":\"public class RegisterService\\n{\\n}\"}],\"testNotes\":\"No test changes\"}"
                      }
                    ]
                  }
                }
              ]
            }
            """);

        using var httpClient = new HttpClient(handler);
        var agent = new GeminiCodeAgent(
            httpClient,
            Options.Create(new AiOptions { Provider = "Gemini", Model = "gemini-2.5-flash" }),
            NullLogger<GeminiCodeAgent>.Instance);

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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }
}
