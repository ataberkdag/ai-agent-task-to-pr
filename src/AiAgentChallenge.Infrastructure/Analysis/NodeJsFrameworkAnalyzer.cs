using System.Text.RegularExpressions;
using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Infrastructure.Analysis;

internal sealed class NodeJsFrameworkAnalyzer : IFrameworkAnalyzerStrategy
{
    private static readonly Regex RouteRegex = new(@"\b(app|router|fastify)\.(?<verb>get|post|put|delete|patch)\s*\(\s*[""`'](?<route>[^""`']+)[""`']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ClassRegex = new(@"\bclass\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private static readonly Regex FunctionRegex = new(@"\bfunction\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(|\b(?:const|let|var)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:async\s*)?\(", RegexOptions.Compiled);

    public bool CanHandle(ProjectDetection detection)
    {
        return string.Equals(detection.BuildTool, "npm", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<FrameworkAnalysisResult> AnalyzeAsync(RepositoryScanContext context, CancellationToken cancellationToken = default)
    {
        var recommended = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var endpoints = new List<ApiEndpointInfo>();
        var symbols = new List<CodeSymbolInfo>();

        foreach (var file in context.Files.Where(file =>
                     string.Equals(Path.GetExtension(file.FullPath), ".js", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(Path.GetExtension(file.FullPath), ".ts", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var content = await File.ReadAllTextAsync(file.FullPath, cancellationToken);
            foreach (var routeMatch in RouteRegex.Matches(content).Cast<Match>())
            {
                var route = NormalizeRoute(routeMatch.Groups["route"].Value);
                endpoints.Add(new ApiEndpointInfo
                {
                    HttpMethod = routeMatch.Groups["verb"].Value.ToUpperInvariant(),
                    Route = route,
                    SourceFile = file.RelativePath,
                    HandlerName = Path.GetFileNameWithoutExtension(file.RelativePath),
                    Style = "Express"
                });
                AddScore(recommended, file.RelativePath, 8);
            }

            foreach (var classMatch in ClassRegex.Matches(content).Cast<Match>())
            {
                symbols.Add(new CodeSymbolInfo
                {
                    Kind = "class",
                    SymbolType = "class",
                    Name = classMatch.Groups["name"].Value,
                    SourceFile = file.RelativePath
                });
            }

            foreach (var functionMatch in FunctionRegex.Matches(content).Cast<Match>())
            {
                var name = functionMatch.Groups["name"].Value;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    symbols.Add(new CodeSymbolInfo
                    {
                        Kind = "function",
                        SymbolType = "function",
                        Name = name,
                        SourceFile = file.RelativePath
                    });
                }
            }
        }

        foreach (var endpoint in endpoints)
        {
            if (context.Keywords.Any(keyword => endpoint.Route.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                AddScore(recommended, endpoint.SourceFile, 10);
            }
        }

        foreach (var file in context.Files.Where(file =>
                     string.Equals(file.FileName, "package.json", StringComparison.OrdinalIgnoreCase) ||
                     file.RelativePath.Contains("/routes/", StringComparison.OrdinalIgnoreCase) ||
                     file.RelativePath.Contains("/controllers/", StringComparison.OrdinalIgnoreCase)))
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
                .Where(file => file.RelativePath.Contains("__tests__", StringComparison.OrdinalIgnoreCase) ||
                               file.RelativePath.Contains("/test/", StringComparison.OrdinalIgnoreCase) ||
                               file.RelativePath.Contains("/tests/", StringComparison.OrdinalIgnoreCase) ||
                               Path.GetFileNameWithoutExtension(file.RelativePath).Contains("spec", StringComparison.OrdinalIgnoreCase))
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
