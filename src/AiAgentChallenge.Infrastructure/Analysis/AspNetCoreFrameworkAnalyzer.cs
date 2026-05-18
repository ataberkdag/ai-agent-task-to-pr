using System.Text;
using System.Text.RegularExpressions;
using AiAgentChallenge.Domain;

namespace AiAgentChallenge.Infrastructure.Analysis;

internal sealed class AspNetCoreFrameworkAnalyzer : IFrameworkAnalyzerStrategy
{
    private static readonly Regex NamespaceRegex = new(@"^\s*namespace\s+([A-Za-z0-9_.]+)", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex TypeRegex = new(@"^\s*(?:public|internal|protected|private)?\s*(?:abstract\s+|sealed\s+|partial\s+)*\b(class|interface|record)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex MethodRegex = new(@"(?<attrs>(?:\s*\[[^\]]+\]\s*)*)^\s*(?:public|internal)\s+(?:static\s+)?(?:async\s+)?(?<returnType>[A-Za-z_][A-Za-z0-9_<>\.\?,\[\]\(\)\s:]*)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<params>[^)]*)\)", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex RouteAttributeRegex = new(@"\[(Route|HttpGet|HttpPost|HttpPut|HttpDelete|HttpPatch)\s*(?:\(\s*""([^""]*)""\s*\))?\]", RegexOptions.Compiled);
    private const string ConstructorRegexTemplate = @"(?<attrs>(?:\s*\[[^\]]+\]\s*)*)^\s*(?:public|internal)\s+{0}\s*\((?<params>[^)]*)\)";
    private static readonly Regex MinimalApiRegex = new(@"\bMap(?<verb>Get|Post|Put|Delete|Patch)\s*\(\s*""(?<route>[^""]+)""", RegexOptions.Compiled);
    private static readonly Regex ObjectCreationRegex = new(@"\bnew\s+(?<name>[A-Z][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled);

    public bool CanHandle(ProjectDetection detection)
    {
        return string.Equals(detection.Framework, "ASP.NET Core", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<FrameworkAnalysisResult> AnalyzeAsync(
        RepositoryScanContext context,
        CancellationToken cancellationToken = default)
    {
        var recommended = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var endpoints = new List<ApiEndpointInfo>();
        var symbols = new List<CodeSymbolInfo>();
        var tests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in context.Files.Where(file => string.Equals(Path.GetExtension(file.FullPath), ".cs", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var content = await File.ReadAllTextAsync(file.FullPath, cancellationToken);
            var fileNamespace = NamespaceRegex.Match(content).Groups[1].Value;
            var typeMatches = TypeRegex.Matches(content).Cast<Match>().ToArray();
            var routeAttributes = RouteAttributeRegex.Matches(content).Cast<Match>().Select(BuildRouteAttributeDisplay).ToArray();
            var referencedTypeNames = ExtractReferencedTypeNames(content);

            foreach (var match in typeMatches)
            {
                var kind = match.Groups[1].Value.ToLowerInvariant();
                var name = match.Groups[2].Value;
                var constructors = ExtractConstructors(content, name, fileNamespace, file.RelativePath, routeAttributes, referencedTypeNames);

                symbols.Add(new CodeSymbolInfo
                {
                    Kind = kind,
                    SymbolType = kind,
                    Name = name,
                    Namespace = fileNamespace,
                    SourceFile = file.RelativePath,
                    ConstructorDependencies = constructors
                        .SelectMany(item => item.Parameters.Select(parameter => parameter.Type))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    RouteAttributes = routeAttributes,
                    ReferencedTypeNames = referencedTypeNames
                });

                symbols.AddRange(constructors);
            }

            foreach (var methodMatch in MethodRegex.Matches(content).Cast<Match>())
            {
                var parameters = ParseParameters(methodMatch.Groups["params"].Value);
                symbols.Add(new CodeSymbolInfo
                {
                    Kind = "method",
                    SymbolType = "method",
                    Name = methodMatch.Groups["name"].Value,
                    Namespace = fileNamespace,
                    SourceFile = file.RelativePath,
                    DisplaySignature = BuildSignature(
                        methodMatch.Groups["name"].Value,
                        methodMatch.Groups["returnType"].Value.Trim(),
                        parameters),
                    ReturnType = methodMatch.Groups["returnType"].Value.Trim(),
                    Parameters = parameters,
                    ConstructorDependencies = Array.Empty<string>(),
                    RouteAttributes = RouteAttributeRegex.Matches(methodMatch.Groups["attrs"].Value)
                        .Cast<Match>()
                        .Select(BuildRouteAttributeDisplay)
                        .ToArray(),
                    ReferencedTypeNames = referencedTypeNames
                });
            }

            var categoryScore = ScoreCategory(file.RelativePath);
            if (categoryScore > 0)
            {
                AddScore(recommended, file.RelativePath, categoryScore);
            }

            if (IsTestFile(file.RelativePath))
            {
                tests.Add(file.RelativePath);
            }

            if (IsControllerFile(file.RelativePath, content))
            {
                AddScore(recommended, file.RelativePath, 8);

                var className = typeMatches.FirstOrDefault(match => string.Equals(match.Groups[1].Value, "class", StringComparison.OrdinalIgnoreCase))?.Groups[2].Value ?? Path.GetFileNameWithoutExtension(file.RelativePath);
                var classRoute = RouteAttributeRegex.Matches(content)
                    .Cast<Match>()
                    .Where(match => string.Equals(match.Groups[1].Value, "Route", StringComparison.OrdinalIgnoreCase))
                    .Select(match => match.Groups[2].Value)
                    .FirstOrDefault() ?? string.Empty;

                foreach (var methodMatch in MethodRegex.Matches(content).Cast<Match>())
                {
                    var attrs = RouteAttributeRegex.Matches(methodMatch.Groups["attrs"].Value).Cast<Match>().ToArray();
                    foreach (var attr in attrs.Where(attr => attr.Groups[1].Value.StartsWith("Http", StringComparison.OrdinalIgnoreCase)))
                    {
                        endpoints.Add(new ApiEndpointInfo
                        {
                            HttpMethod = attr.Groups[1].Value["Http".Length..].ToUpperInvariant(),
                            Route = NormalizeRoute(CombineRoutes(classRoute, attr.Groups[2].Value), className),
                            SourceFile = file.RelativePath,
                            HandlerName = $"{className}.{methodMatch.Groups["name"].Value}",
                            Style = "Controller"
                        });
                    }
                }
            }

            foreach (var minimalMatch in MinimalApiRegex.Matches(content).Cast<Match>())
            {
                endpoints.Add(new ApiEndpointInfo
                {
                    HttpMethod = minimalMatch.Groups["verb"].Value.ToUpperInvariant(),
                    Route = NormalizeRoute(minimalMatch.Groups["route"].Value, Path.GetFileNameWithoutExtension(file.RelativePath)),
                    SourceFile = file.RelativePath,
                    HandlerName = Path.GetFileNameWithoutExtension(file.RelativePath),
                    Style = "MinimalApi"
                });
                AddScore(recommended, file.RelativePath, 8);
            }
        }

        foreach (var endpoint in endpoints)
        {
            var endpointKeywords = endpoint.Route.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(segment => segment.Trim().ToLowerInvariant())
                .Where(segment => segment.Length >= 3)
                .ToArray();

            if (endpointKeywords.Any(keyword => context.Keywords.Contains(keyword)))
            {
                AddScore(recommended, endpoint.SourceFile, 10);
                BoostRelatedFiles(recommended, context.Files, endpointKeywords);
            }
        }

        return new FrameworkAnalysisResult
        {
            RecommendedFiles = recommended
                .OrderByDescending(item => item.Value)
                .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => item.Key)
                .Take(20)
                .ToArray(),
            ExistingTestFiles = tests.Take(20).ToArray(),
            ApiEndpoints = endpoints
                .OrderBy(endpoint => endpoint.Route, StringComparer.OrdinalIgnoreCase)
                .ThenBy(endpoint => endpoint.HttpMethod, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Symbols = symbols
                .OrderBy(symbol => symbol.SourceFile, StringComparer.OrdinalIgnoreCase)
                .ThenBy(symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static void BoostRelatedFiles(
        Dictionary<string, int> recommended,
        IReadOnlyList<AnalyzedRepositoryFile> files,
        IReadOnlyCollection<string> endpointKeywords)
    {
        foreach (var file in files)
        {
            var fileName = Path.GetFileNameWithoutExtension(file.RelativePath);
            var normalizedPath = file.RelativePath.ToLowerInvariant();
            var score = 0;

            if (endpointKeywords.Any(keyword => normalizedPath.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                score += 4;
            }

            if (fileName.EndsWith("Request", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith("Response", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith("Dto", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith("Model", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith("Validator", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith("Service", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith("Repository", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith("Test", StringComparison.OrdinalIgnoreCase))
            {
                score += 3;
            }

            if (score > 0)
            {
                AddScore(recommended, file.RelativePath, score);
            }
        }
    }

    private static IReadOnlyList<CodeSymbolInfo> ExtractConstructors(
        string content,
        string typeName,
        string fileNamespace,
        string relativePath,
        IReadOnlyList<string> routeAttributes,
        IReadOnlyList<string> referencedTypeNames)
    {
        var regex = new Regex(string.Format(ConstructorRegexTemplate, Regex.Escape(typeName)), RegexOptions.Multiline | RegexOptions.Compiled);
        return regex.Matches(content)
            .Cast<Match>()
            .Select(match =>
            {
                var parameters = ParseParameters(match.Groups["params"].Value);
                return new CodeSymbolInfo
                {
                    Kind = "constructor",
                    SymbolType = "constructor",
                    Name = typeName,
                    Namespace = fileNamespace,
                    SourceFile = relativePath,
                    DisplaySignature = BuildSignature(typeName, string.Empty, parameters),
                    Parameters = parameters,
                    ConstructorDependencies = parameters
                        .Select(parameter => parameter.Type)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    RouteAttributes = routeAttributes,
                    ReferencedTypeNames = referencedTypeNames
                };
            })
            .ToArray();
    }

    private static IReadOnlyList<string> ExtractReferencedTypeNames(string content)
    {
        return ObjectCreationRegex.Matches(content)
            .Cast<Match>()
            .Select(match => match.Groups["name"].Value.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<CodeParameterInfo> ParseParameters(string rawParameters)
    {
        if (string.IsNullOrWhiteSpace(rawParameters))
        {
            return Array.Empty<CodeParameterInfo>();
        }

        var parts = SplitParameters(rawParameters);
        var parameters = new List<CodeParameterInfo>(parts.Count);

        for (var index = 0; index < parts.Count; index++)
        {
            var parsed = TryParseParameter(parts[index], index);
            if (parsed is not null)
            {
                parameters.Add(parsed);
            }
        }

        return parameters;
    }

    private static List<string> SplitParameters(string rawParameters)
    {
        var parameters = new List<string>();
        var builder = new StringBuilder();
        var genericDepth = 0;
        var parenthesesDepth = 0;
        var bracketDepth = 0;

        foreach (var character in rawParameters)
        {
            switch (character)
            {
                case '<':
                    genericDepth++;
                    break;
                case '>':
                    genericDepth = Math.Max(0, genericDepth - 1);
                    break;
                case '(':
                    parenthesesDepth++;
                    break;
                case ')':
                    parenthesesDepth = Math.Max(0, parenthesesDepth - 1);
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    bracketDepth = Math.Max(0, bracketDepth - 1);
                    break;
                case ',' when genericDepth == 0 && parenthesesDepth == 0 && bracketDepth == 0:
                    AddParameter();
                    continue;
            }

            builder.Append(character);
        }

        AddParameter();
        return parameters;

        void AddParameter()
        {
            var value = builder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                parameters.Add(value);
            }

            builder.Clear();
        }
    }

    private static CodeParameterInfo? TryParseParameter(string rawParameter, int ordinal)
    {
        var isOptional = rawParameter.Contains('=', StringComparison.Ordinal);
        var parameterWithoutDefault = rawParameter.Split('=')[0].Trim();
        var tokens = parameterWithoutDefault
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !IsParameterModifier(token))
            .ToArray();

        if (tokens.Length < 2)
        {
            return null;
        }

        var name = tokens[^1];
        var type = string.Join(' ', tokens[..^1]);
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        return new CodeParameterInfo
        {
            Name = name,
            Type = type,
            Ordinal = ordinal,
            IsOptional = isOptional
        };
    }

    private static bool IsParameterModifier(string token)
    {
        return string.Equals(token, "this", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(token, "ref", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(token, "out", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(token, "in", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(token, "params", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSignature(string name, string returnType, IReadOnlyList<CodeParameterInfo> parameters)
    {
        var parameterText = string.Join(", ", parameters
            .OrderBy(parameter => parameter.Ordinal)
            .Select(parameter => $"{parameter.Type} {parameter.Name}"));

        return string.IsNullOrWhiteSpace(returnType)
            ? $"{name}({parameterText})"
            : $"{returnType} {name}({parameterText})";
    }

    private static bool IsControllerFile(string relativePath, string content)
    {
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        return fileName.EndsWith("Controller", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("[ApiController]", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("ControllerBase", StringComparison.OrdinalIgnoreCase);
    }

    private static int ScoreCategory(string relativePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        if (fileName.EndsWith("Controller", StringComparison.OrdinalIgnoreCase))
        {
            return 7;
        }

        if (fileName.EndsWith("Request", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("Response", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("Dto", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("Model", StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        if (fileName.EndsWith("Validator", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("Service", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("Repository", StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        if (IsTestFile(relativePath))
        {
            return 3;
        }

        return 0;
    }

    private static bool IsTestFile(string relativePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        return relativePath.Contains("/tests/", StringComparison.OrdinalIgnoreCase) ||
               relativePath.Contains("/test/", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("Tests", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("Spec", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("Should", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddScore(IDictionary<string, int> scores, string relativePath, int amount)
    {
        scores[relativePath] = scores.TryGetValue(relativePath, out var current) ? current + amount : amount;
    }

    private static string BuildRouteAttributeDisplay(Match match)
    {
        var name = match.Groups[1].Value;
        var value = match.Groups[2].Value;
        return string.IsNullOrWhiteSpace(value) ? name : $"{name}(\"{value}\")";
    }

    private static string CombineRoutes(string classRoute, string methodRoute)
    {
        if (string.IsNullOrWhiteSpace(classRoute))
        {
            return methodRoute;
        }

        if (string.IsNullOrWhiteSpace(methodRoute))
        {
            return classRoute;
        }

        return $"{classRoute.TrimEnd('/')}/{methodRoute.TrimStart('/')}";
    }

    private static string NormalizeRoute(string route, string className)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return "/";
        }

        var normalized = route.Replace("[controller]", className.Replace("Controller", string.Empty, StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        normalized = normalized.StartsWith('/') ? normalized : $"/{normalized}";
        return normalized;
    }
}
