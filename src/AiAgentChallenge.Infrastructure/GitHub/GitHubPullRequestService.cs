using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AiAgentChallenge.Application.Abstractions;
using AiAgentChallenge.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiAgentChallenge.Infrastructure.GitHub;

public sealed class GitHubPullRequestService : IPullRequestService
{
    private readonly HttpClient _httpClient;
    private readonly IGitHubRepositoryParser _repositoryParser;
    private readonly GitHubOptions _options;
    private readonly ILogger<GitHubPullRequestService> _logger;

    public GitHubPullRequestService(
        HttpClient httpClient,
        IGitHubRepositoryParser repositoryParser,
        IOptions<GitHubOptions> options,
        ILogger<GitHubPullRequestService> logger)
    {
        _httpClient = httpClient;
        _repositoryParser = repositoryParser;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PullRequestResult> CreateOrGetPullRequestAsync(
        string repositoryUrl,
        string baseBranch,
        string headBranch,
        string title,
        string body,
        CancellationToken cancellationToken = default)
    {
        var token = Environment.GetEnvironmentVariable(_options.TokenEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException($"{_options.TokenEnvironmentVariable} environment variable is not configured.");
        }

        var (owner, repositoryName) = _repositoryParser.Parse(repositoryUrl);
        var head = $"{owner}:{headBranch}";

        using var existingRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_options.ApiBaseUrl.TrimEnd('/')}/repos/{owner}/{repositoryName}/pulls?state=open&head={Uri.EscapeDataString(head)}");
        PrepareHeaders(existingRequest, token);

        using var existingResponse = await _httpClient.SendAsync(existingRequest, cancellationToken);
        var existingContent = await existingResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!existingResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GitHub PR lookup failed with status {(int)existingResponse.StatusCode}.");
        }

        using (var document = JsonDocument.Parse(existingContent))
        {
            if (document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.GetArrayLength() > 0)
            {
                var existing = document.RootElement[0];
                _logger.LogInformation("Reusing existing pull request for {Owner}/{Repository} head {HeadBranch}", owner, repositoryName, headBranch);
                return new PullRequestResult
                {
                    PullRequestUrl = existing.GetProperty("html_url").GetString() ?? string.Empty,
                    PullRequestNumber = existing.GetProperty("number").GetInt32(),
                    Status = PullRequestStatus.AlreadyExists
                };
            }
        }

        using var createRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.ApiBaseUrl.TrimEnd('/')}/repos/{owner}/{repositoryName}/pulls");
        PrepareHeaders(createRequest, token);
        createRequest.Content = JsonContent.Create(new
        {
            title,
            head = headBranch,
            @base = baseBranch,
            body
        });

        _logger.LogInformation("Creating pull request for {Owner}/{Repository} head {HeadBranch}", owner, repositoryName, headBranch);

        using var createResponse = await _httpClient.SendAsync(createRequest, cancellationToken);
        var createContent = await createResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!createResponse.IsSuccessStatusCode)
        {
            _logger.LogError("GitHub PR creation failed for {Owner}/{Repository} with status {StatusCode}", owner, repositoryName, (int)createResponse.StatusCode);
            throw new InvalidOperationException($"GitHub PR creation failed with status {(int)createResponse.StatusCode}.");
        }

        using var createdDocument = JsonDocument.Parse(createContent);
        return new PullRequestResult
        {
            PullRequestUrl = createdDocument.RootElement.GetProperty("html_url").GetString() ?? string.Empty,
            PullRequestNumber = createdDocument.RootElement.GetProperty("number").GetInt32(),
            Status = PullRequestStatus.Created
        };
    }

    private static void PrepareHeaders(HttpRequestMessage request, string token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AiAgentChallenge", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }
}
