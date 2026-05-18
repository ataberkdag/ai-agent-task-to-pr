using System.Text;
using System.Text.RegularExpressions;

namespace AiAgentChallenge.Infrastructure.Analysis;

internal enum DependencyTokenKind
{
    InjectedType,
    ConstructedType,
    ParameterType,
    ReturnType,
    BaseType,
    ImplementedInterface
}

internal sealed record DependencyToken(DependencyTokenKind Kind, string TypeName);

internal sealed class DotNetDependencyIndex
{
    public IReadOnlyDictionary<string, IReadOnlyList<string>> TypeToFiles { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, IReadOnlyList<string>> InterfaceToImplementationFiles { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> InterfaceToRegisteredImplementationType { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, IReadOnlyList<string>> BaseTypeToDerivedTypeNames { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, IReadOnlyList<DependencyToken>> FileDependencies { get; init; } =
        new Dictionary<string, IReadOnlyList<DependencyToken>>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, IReadOnlyList<string>> FileDefinedTypeNames { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> TypeToBaseType { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

internal static class DotNetDependencyIndexBuilder
{
    private static readonly Regex TypeDeclarationRegex = new(
        @"^\s*(?:public|internal|protected|private)?\s*(?:abstract\s+|sealed\s+|partial\s+)*\b(class|interface|record)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*(?<bases>[^{\r\n]+))?",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex ConstructorRegex = new(
        @"^\s*(?:public|internal)\s+(?<name>[A-Z][A-Za-z0-9_]*)\s*\((?<params>[^)]*)\)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex MethodRegex = new(
        @"(?<attrs>(?:\s*\[[^\]]+\]\s*)*)^\s*(?:public|internal)\s+(?:static\s+)?(?:async\s+)?(?<returnType>[A-Za-z_][A-Za-z0-9_<>\.\?,\[\]\(\)\s:]*)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<params>[^)]*)\)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex ObjectCreationRegex = new(@"\bnew\s+(?<name>[A-Z][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled);

    private static readonly Regex GenericDiRegistrationRegex = new(
        @"\bAdd(?:Scoped|Transient|Singleton)\s*<\s*(?<service>[A-Z][A-Za-z0-9_]*)\s*,\s*(?<implementation>[A-Z][A-Za-z0-9_]*)\s*>",
        RegexOptions.Compiled);

    private static readonly HashSet<string> IgnoredTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Task",
        "ValueTask",
        "ActionResult",
        "IActionResult",
        "IResult",
        "Results",
        "Result",
        "Guid",
        "DateTime",
        "DateOnly",
        "TimeOnly",
        "TimeSpan",
        "CancellationToken",
        "String",
        "Int32",
        "Int64",
        "Int16",
        "Boolean",
        "Decimal",
        "Double",
        "Single",
        "Object",
        "Exception",
        "IServiceCollection",
        "IServiceProvider",
        "ControllerBase",
        "ApiController",
        "Route",
        "HttpGet",
        "HttpPost",
        "HttpPut",
        "HttpDelete",
        "HttpPatch",
        "FromBody",
        "FromRoute",
        "FromQuery",
        "FromServices",
        "ProducesResponseType",
        "Produces",
        "Consumes",
        "Ok",
        "BadRequest",
        "NotFound",
        "Created",
        "NoContent"
    };

    public static DotNetDependencyIndex Build(IReadOnlyList<AnalyzedRepositoryFile> files)
    {
        var typeToFiles = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var interfaceToImplementationFiles = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var interfaceToRegisteredImplementationType = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var baseTypeToDerivedTypeNames = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var fileDependencies = new Dictionary<string, IReadOnlyList<DependencyToken>>(StringComparer.OrdinalIgnoreCase);
        var fileDefinedTypeNames = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var typeToBaseType = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files.Where(file => string.Equals(Path.GetExtension(file.FullPath), ".cs", StringComparison.OrdinalIgnoreCase)))
        {
            var content = File.ReadAllText(file.FullPath);
            var extracted = ExtractFileInfo(content);

            fileDependencies[file.RelativePath] = extracted.Dependencies;
            fileDefinedTypeNames[file.RelativePath] = extracted.DefinedTypeNames;

            foreach (var typeName in extracted.DefinedTypeNames)
            {
                AddValue(typeToFiles, typeName, file.RelativePath);
            }

            foreach (var registration in extracted.Registrations)
            {
                interfaceToRegisteredImplementationType[registration.ServiceType] = registration.ImplementationType;
            }

            foreach (var typeDefinition in extracted.TypeDefinitions)
            {
                if (!string.IsNullOrWhiteSpace(typeDefinition.BaseType))
                {
                    typeToBaseType[typeDefinition.Name] = typeDefinition.BaseType;
                    AddValue(baseTypeToDerivedTypeNames, typeDefinition.BaseType, typeDefinition.Name);
                }

                foreach (var implementedInterface in typeDefinition.ImplementedInterfaces)
                {
                    AddValue(interfaceToImplementationFiles, implementedInterface, file.RelativePath);
                }
            }
        }

        return new DotNetDependencyIndex
        {
            TypeToFiles = typeToFiles.ToDictionary(
                item => item.Key,
                item => (IReadOnlyList<string>)item.Value.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase),
            InterfaceToImplementationFiles = interfaceToImplementationFiles.ToDictionary(
                item => item.Key,
                item => (IReadOnlyList<string>)item.Value.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase),
            InterfaceToRegisteredImplementationType = interfaceToRegisteredImplementationType,
            BaseTypeToDerivedTypeNames = baseTypeToDerivedTypeNames.ToDictionary(
                item => item.Key,
                item => (IReadOnlyList<string>)item.Value.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase),
            FileDependencies = fileDependencies,
            FileDefinedTypeNames = fileDefinedTypeNames,
            TypeToBaseType = typeToBaseType
        };
    }

    private static ExtractedFileInfo ExtractFileInfo(string content)
    {
        var dependencies = new HashSet<DependencyToken>();
        var definedTypeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var typeDefinitions = new List<TypeDefinitionInfo>();
        var registrations = new List<ServiceRegistrationInfo>();

        foreach (Match match in TypeDeclarationRegex.Matches(content))
        {
            var typeName = match.Groups["name"].Value.Trim();
            if (string.IsNullOrWhiteSpace(typeName))
            {
                continue;
            }

            definedTypeNames.Add(typeName);

            var bases = match.Groups["bases"].Value;
            var baseType = string.Empty;
            var implementedInterfaces = new List<string>();

            foreach (var candidate in ExtractCandidateTypeNames(bases))
            {
                if (candidate.StartsWith("I", StringComparison.Ordinal) &&
                    candidate.Length > 1 &&
                    char.IsUpper(candidate[1]))
                {
                    implementedInterfaces.Add(candidate);
                    dependencies.Add(new DependencyToken(DependencyTokenKind.ImplementedInterface, candidate));
                }
                else if (string.IsNullOrWhiteSpace(baseType))
                {
                    baseType = candidate;
                    dependencies.Add(new DependencyToken(DependencyTokenKind.BaseType, candidate));
                }
            }

            typeDefinitions.Add(new TypeDefinitionInfo(typeName, baseType, implementedInterfaces));
        }

        foreach (Match match in ConstructorRegex.Matches(content))
        {
            foreach (var parameterType in ExtractParameterTypeNames(match.Groups["params"].Value))
            {
                dependencies.Add(new DependencyToken(DependencyTokenKind.InjectedType, parameterType));
            }
        }

        foreach (Match match in MethodRegex.Matches(content))
        {
            foreach (var parameterType in ExtractParameterTypeNames(match.Groups["params"].Value))
            {
                dependencies.Add(new DependencyToken(DependencyTokenKind.ParameterType, parameterType));
            }

            foreach (var returnType in ExtractCandidateTypeNames(match.Groups["returnType"].Value))
            {
                dependencies.Add(new DependencyToken(DependencyTokenKind.ReturnType, returnType));
            }
        }

        foreach (Match match in ObjectCreationRegex.Matches(content))
        {
            var typeName = match.Groups["name"].Value.Trim();
            if (IsRelevantTypeName(typeName))
            {
                dependencies.Add(new DependencyToken(DependencyTokenKind.ConstructedType, typeName));
            }
        }

        foreach (Match match in GenericDiRegistrationRegex.Matches(content))
        {
            var serviceType = match.Groups["service"].Value.Trim();
            var implementationType = match.Groups["implementation"].Value.Trim();
            if (!IsRelevantTypeName(serviceType) || !IsRelevantTypeName(implementationType))
            {
                continue;
            }

            registrations.Add(new ServiceRegistrationInfo(serviceType, implementationType));
        }

        return new ExtractedFileInfo(
            dependencies.OrderBy(token => token.TypeName, StringComparer.OrdinalIgnoreCase).ToArray(),
            definedTypeNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
            typeDefinitions,
            registrations);
    }

    private static IReadOnlyList<string> ExtractParameterTypeNames(string rawParameters)
    {
        if (string.IsNullOrWhiteSpace(rawParameters))
        {
            return Array.Empty<string>();
        }

        var parameters = SplitParameters(rawParameters);
        var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in parameters)
        {
            var sanitized = Regex.Replace(parameter, @"\[[^\]]+\]", " ").Split('=')[0].Trim();
            var tokens = sanitized
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(token => !IsParameterModifier(token))
                .ToArray();

            if (tokens.Length < 2)
            {
                continue;
            }

            var typePortion = string.Join(' ', tokens[..^1]);
            foreach (var typeName in ExtractCandidateTypeNames(typePortion))
            {
                types.Add(typeName);
            }
        }

        return types.ToArray();
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

    private static bool IsParameterModifier(string token)
    {
        return string.Equals(token, "this", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(token, "ref", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(token, "out", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(token, "in", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(token, "params", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ExtractCandidateTypeNames(string rawType)
    {
        if (string.IsNullOrWhiteSpace(rawType))
        {
            return Array.Empty<string>();
        }

        var matches = Regex.Matches(rawType, @"\b[A-Z][A-Za-z0-9_]*\b")
            .Cast<Match>()
            .Select(match => match.Value.Trim())
            .Where(IsRelevantTypeName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return matches;
    }

    private static bool IsRelevantTypeName(string typeName)
    {
        return !string.IsNullOrWhiteSpace(typeName) &&
               !IgnoredTypeNames.Contains(typeName) &&
               !string.Equals(typeName, "T", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddValue(IDictionary<string, HashSet<string>> dictionary, string key, string value)
    {
        if (!dictionary.TryGetValue(key, out var values))
        {
            values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            dictionary[key] = values;
        }

        values.Add(value);
    }

    private sealed record ExtractedFileInfo(
        IReadOnlyList<DependencyToken> Dependencies,
        IReadOnlyList<string> DefinedTypeNames,
        IReadOnlyList<TypeDefinitionInfo> TypeDefinitions,
        IReadOnlyList<ServiceRegistrationInfo> Registrations);

    private sealed record TypeDefinitionInfo(
        string Name,
        string BaseType,
        IReadOnlyList<string> ImplementedInterfaces);

    private sealed record ServiceRegistrationInfo(
        string ServiceType,
        string ImplementationType);
}
