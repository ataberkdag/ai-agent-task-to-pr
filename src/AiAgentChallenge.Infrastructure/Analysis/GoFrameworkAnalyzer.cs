using System.Text.RegularExpressions;
using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Infrastructure.Analysis;

internal sealed class GoFrameworkAnalyzer : IFrameworkAnalyzerStrategy
{
    private static readonly Regex RouterRouteRegex = new(@"(?:router|r)\.(?<verb>GET|POST|PUT|DELETE|PATCH)\s*\(\s*""(?<route>[^""]+)""", RegexOptions.Compiled);
    private static readonly Regex HandleFuncRegex = new(@"http\.HandleFunc\(\s*""(?<route>[^""]+)""\s*,\s*(?<handler>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private static readonly Regex PackageRegex = new(@"^\s*package\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex TypeRegex = new(@"^\s*type\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s+(?<kind>struct|interface)", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex FuncRegex = new(@"^\s*func(?:\s*\([^)]+\))?\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Multiline | RegexOptions.Compiled);

    public bool CanHandle(ProjectDetection detection)
    {
        return string.Equals(detection.Language, "Go", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<FrameworkAnalysisResult> AnalyzeAsync(RepositoryScanContext context, CancellationToken cancellationToken = default)
    {
        var recommended = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var endpoints = new List<ApiEndpointInfo>();
        var symbols = new List<CodeSymbolInfo>();

        foreach (var file in context.Files.Where(file => string.Equals(Path.GetExtension(file.FullPath), ".go", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file.FullPath, cancellationToken);
            var packageName = PackageRegex.Match(content).Groups["name"].Value;

            foreach (var routeMatch in RouterRouteRegex.Matches(content).Cast<Match>())
            {
                endpoints.Add(new ApiEndpointInfo
                {
                    HttpMethod = routeMatch.Groups["verb"].Value.ToUpperInvariant(),
                    Route = NormalizeRoute(routeMatch.Groups["route"].Value),
                    SourceFile = file.RelativePath,
                    HandlerName = Path.GetFileNameWithoutExtension(file.RelativePath),
                    Style = "Gin"
                });
                AddScore(recommended, file.RelativePath, 8);
            }

            foreach (var routeMatch in HandleFuncRegex.Matches(content).Cast<Match>())
            {
                endpoints.Add(new ApiEndpointInfo
                {
                    HttpMethod = "ANY",
                    Route = NormalizeRoute(routeMatch.Groups["route"].Value),
                    SourceFile = file.RelativePath,
                    HandlerName = routeMatch.Groups["handler"].Value,
                    Style = "net/http"
                });
                AddScore(recommended, file.RelativePath, 8);
            }

            foreach (var typeMatch in TypeRegex.Matches(content).Cast<Match>())
            {
                symbols.Add(new CodeSymbolInfo
                {
                    Kind = typeMatch.Groups["kind"].Value.ToLowerInvariant(),
                    SymbolType = typeMatch.Groups["kind"].Value.ToLowerInvariant(),
                    Name = typeMatch.Groups["name"].Value,
                    Namespace = packageName,
                    SourceFile = file.RelativePath
                });
            }

            foreach (var funcMatch in FuncRegex.Matches(content).Cast<Match>())
            {
                symbols.Add(new CodeSymbolInfo
                {
                    Kind = "method",
                    SymbolType = "method",
                    Name = funcMatch.Groups["name"].Value,
                    Namespace = packageName,
                    SourceFile = file.RelativePath
                });
            }

            if (file.RelativePath.Contains("/handlers/", StringComparison.OrdinalIgnoreCase) ||
                file.RelativePath.Contains("/routes/", StringComparison.OrdinalIgnoreCase))
            {
                AddScore(recommended, file.RelativePath, 4);
            }
        }

        foreach (var endpoint in endpoints)
        {
            if (context.Keywords.Any(keyword => endpoint.Route.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                AddScore(recommended, endpoint.SourceFile, 10);
            }
        }

        foreach (var file in context.Files.Where(file => string.Equals(file.FileName, "go.mod", StringComparison.OrdinalIgnoreCase)))
        {
            AddScore(recommended, file.RelativePath, 4);
        }

        return new FrameworkAnalysisResult
        {
            RecommendedFiles = recommended
                .OrderByDescending(item => item.Value)
                .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => item.Key)
                .Take(20)
                .ToArray(),
            ExistingTestFiles = context.Files
                .Where(file => file.RelativePath.EndsWith("_test.go", StringComparison.OrdinalIgnoreCase))
                .Select(file => file.RelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToArray(),
            ApiEndpoints = endpoints.ToArray(),
            Symbols = symbols.ToArray()
        };
    }

    private static void AddScore(IDictionary<string, int> scores, string relativePath, int amount)
    {
        scores[relativePath] = scores.TryGetValue(relativePath, out var current) ? current + amount : amount;
    }

    private static string NormalizeRoute(string route)
    {
        return route.StartsWith('/') ? route : $"/{route}";
    }
}
