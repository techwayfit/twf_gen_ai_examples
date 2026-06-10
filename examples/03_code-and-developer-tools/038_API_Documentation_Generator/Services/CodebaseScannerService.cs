using System.Text.RegularExpressions;
using _038_API_Documentation_Generator.Models;

namespace _038_API_Documentation_Generator.Services;

public sealed class CodebaseScannerService
{
    private static readonly HashSet<string> LanguageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".java", ".go", ".rb", ".rs", ".php", ".kt", ".swift"
    };

    private static readonly Dictionary<string, HashSet<string>> LanguageFilePatterns = new()
    {
        ["csharp"]   = new(StringComparer.OrdinalIgnoreCase) { ".cs" },
        ["typescript"] = new(StringComparer.OrdinalIgnoreCase) { ".ts", ".tsx" },
        ["javascript"] = new(StringComparer.OrdinalIgnoreCase) { ".js", ".jsx" },
        ["python"]   = new(StringComparer.OrdinalIgnoreCase) { ".py" },
        ["java"]     = new(StringComparer.OrdinalIgnoreCase) { ".java" },
        ["go"]       = new(StringComparer.OrdinalIgnoreCase) { ".go" },
        ["ruby"]     = new(StringComparer.OrdinalIgnoreCase) { ".rb" },
        ["rust"]     = new(StringComparer.OrdinalIgnoreCase) { ".rs" },
        ["php"]      = new(StringComparer.OrdinalIgnoreCase) { ".php" },
        ["kotlin"]   = new(StringComparer.OrdinalIgnoreCase) { ".kt" },
        ["swift"]    = new(StringComparer.OrdinalIgnoreCase) { ".swift" }
    };

    public List<SourceFile> ScanCodebase(string repoPath, List<string>? languageFilters, int maxFiles)
    {
        if (!Directory.Exists(repoPath))
            throw new DirectoryNotFoundException($"Repository path not found: {repoPath}");

        var files = new List<SourceFile>();
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (languageFilters != null && languageFilters.Count > 0)
        {
            foreach (var lang in languageFilters)
            {
                if (LanguageFilePatterns.TryGetValue(lang.ToLowerInvariant(), out var exts))
                    extensions.UnionWith(exts);
            }
        }
        else
        {
            extensions = LanguageExtensions;
        }

        var filePaths = Directory.EnumerateFiles(repoPath, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f)))
            .Where(f => !IsExcludedPath(f))
            .Take(maxFiles)
            .ToList();

        foreach (var filePath in filePaths)
        {
            try
            {
                var content = File.ReadAllText(filePath);
                var relativePath = Path.GetRelativePath(repoPath, filePath);
                var ext = Path.GetExtension(filePath);
                var language = LanguageFilePatterns
                    .FirstOrDefault(kvp => kvp.Value.Contains(ext)).Key ?? "unknown";

                var sourceFile = new SourceFile
                {
                    FilePath = filePath,
                    RelativePath = relativePath,
                    Language = language,
                    Content = content,
                    Functions = ExtractFunctions(content, language, filePath),
                    IsController = DetectController(content, language),
                    RoutePrefix = ExtractRoutePrefix(content, language),
                    ClassName = ExtractClassName(content, language),
                    Namespace = ExtractNamespace(content, language)
                };

                if (sourceFile.Functions.Count > 0)
                    files.Add(sourceFile);
            }
            catch
            {
                // Skip files that cannot be read
            }
        }

        return files;
    }

    private static bool IsExcludedPath(string path)
    {
        var segment = path.Replace('\\', '/');
        return segment.Contains("/bin/") || segment.Contains("/obj/") ||
               segment.Contains("/node_modules/") || segment.Contains("/.git/") ||
               segment.Contains("/dist/") || segment.Contains("/build/") ||
               segment.Contains("/.vs/") || segment.Contains("/packages/");
    }

    private static List<ApiFunction> ExtractFunctions(string content, string language, string filePath)
    {
        var functions = new List<ApiFunction>();
        var lines = content.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            if (language == "csharp")
                TryExtractCSharpFunction(lines, i, line, functions);
            else if (language is "typescript" or "javascript")
                TryExtractTsJsFunction(lines, i, line, functions);
            else if (language == "python")
                TryExtractPythonFunction(lines, i, line, functions);
            else if (language == "java")
                TryExtractJavaFunction(lines, i, line, functions);
            else if (language == "go")
                TryExtractGoFunction(lines, i, line, functions);
        }

        return functions;
    }

    private static void TryExtractCSharpFunction(string[] lines, int i, string line, List<ApiFunction> functions)
    {
        var publicMethodMatch = Regex.Match(line, @"^\s*(public|internal|protected)\s+(virtual\s+|override\s+|async\s+)?(static\s+)?(partial\s+)?(\w+[?]?|<[^>]+>|Task\s*<[^>]+>|Task|void|string|int|bool|long|double|decimal|float|char|byte|Guid|DateTime|object)\s+(\w+)\s*\(");
        if (!publicMethodMatch.Success) return;

        var name = publicMethodMatch.Groups[6].Value;
        var returnType = publicMethodMatch.Groups[5].Value;
        var visibility = publicMethodMatch.Groups[1].Value;

        var xmlDocSummary = ExtractXmlDocSummary(lines, i);
        var xmlDocReturns = ExtractXmlDocReturns(lines, i);
        var httpMethod = ExtractHttpAttribute(lines, i);
        var route = ExtractRouteAttribute(line);

        var func = new ApiFunction
        {
            Name = name,
            Declaration = line.Trim(),
            Visibility = visibility,
            ReturnType = returnType,
            LineNumber = i + 1,
            XmlDocSummary = xmlDocSummary,
            XmlDocReturns = xmlDocReturns,
            HttpMethod = httpMethod,
            RouteTemplate = route,
            IsControllerAction = httpMethod != null || route != null,
            Parameters = ExtractCSharpParameters(line)
        };

        functions.Add(func);
    }

    private static void TryExtractTsJsFunction(string[] lines, int i, string line, List<ApiFunction> functions)
    {
        var match = Regex.Match(line, @"^\s*(export\s+)?(async\s+)?(function\s+(\w+)|const\s+(\w+)\s*=\s*(async\s*)?\(|\w+\s*\([^)]*\)\s*\{)");
        if (!match.Success) return;

        var name = match.Groups[4].Success ? match.Groups[4].Value
                 : match.Groups[5].Success ? match.Groups[5].Value : "anonymous";

        var func = new ApiFunction
        {
            Name = name,
            Declaration = line.Trim(),
            Visibility = match.Groups[1].Success ? "exported" : "internal",
            ReturnType = "any",
            LineNumber = i + 1,
            IsControllerAction = DetectExpressRoute(lines, i),
            RouteTemplate = ExtractExpressRoute(line)
        };

        functions.Add(func);
    }

    private static void TryExtractPythonFunction(string[] lines, int i, string line, List<ApiFunction> functions)
    {
        var match = Regex.Match(line, @"^\s*(async\s+)?def\s+(\w+)\s*\(");
        if (!match.Success) return;

        var name = match.Groups[2].Value;
        var isAsync = match.Groups[1].Success;

        var func = new ApiFunction
        {
            Name = name,
            Declaration = line.Trim(),
            Visibility = name.StartsWith("_") ? "private" : "public",
            ReturnType = isAsync ? "Awaitable" : "object",
            LineNumber = i + 1,
            IsControllerAction = DetectFlaskDjangoRoute(lines, i),
            RouteTemplate = ExtractFlaskDjangoRoute(line)
        };

        functions.Add(func);
    }

    private static void TryExtractJavaFunction(string[] lines, int i, string line, List<ApiFunction> functions)
    {
        var match = Regex.Match(line, @"^\s*(public|private|protected)\s+(static\s+)?(final\s+)?(\w+[?]?|<[^>]+>|void|String|int|boolean|long|double)\s+(\w+)\s*\(");
        if (!match.Success) return;

        var annotations = ExtractJavaAnnotations(lines, i);

        var func = new ApiFunction
        {
            Name = match.Groups[5].Value,
            Declaration = line.Trim(),
            Visibility = match.Groups[1].Value,
            ReturnType = match.Groups[4].Value,
            LineNumber = i + 1,
            IsControllerAction = annotations.Any(a => a.StartsWith("@")),
            HttpMethod = ExtractJavaHttpMethod(annotations),
            RouteTemplate = ExtractJavaRequestMapping(annotations)
        };

        functions.Add(func);
    }

    private static void TryExtractGoFunction(string[] lines, int i, string line, List<ApiFunction> functions)
    {
        var match = Regex.Match(line, @"^\s*func\s+(\([^)]+\)\s+)?(\w+)\s*\(");
        if (!match.Success) return;

        var func = new ApiFunction
        {
            Name = match.Groups[2].Value,
            Declaration = line.Trim(),
            Visibility = char.IsUpper(match.Groups[2].Value[0]) ? "public" : "private",
            ReturnType = "any",
            LineNumber = i + 1,
            IsControllerAction = char.IsUpper(match.Groups[2].Value[0])
        };

        functions.Add(func);
    }

    private static List<FunctionParameter> ExtractCSharpParameters(string line)
    {
        var paramsMatch = Regex.Match(line, @"\(([^)]*)\)");
        if (!paramsMatch.Success) return new();

        var paramParts = paramsMatch.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries);
        var parameters = new List<FunctionParameter>();

        foreach (var part in paramParts)
        {
            if (string.IsNullOrWhiteSpace(part)) continue;

            var attributeMatch = Regex.Match(part, @"\[FromBody\]|\[FromQuery\]|\[FromRoute\]");
            var isFromBody = attributeMatch.Value == "[FromBody]";
            var isFromQuery = attributeMatch.Value == "[FromQuery]";
            var isFromRoute = attributeMatch.Value == "[FromRoute]";

            var cleaned = Regex.Replace(part, @"\[From(Body|Query|Route)\]", "").Trim();
            var parts = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 2)
            {
                var defaultValue = "";
                var hasDefault = false;
                var namePart = parts[^1];
                var typePart = string.Join(" ", parts.Take(parts.Length - 1));

                var defaultMatch = Regex.Match(namePart, @"(\w+)\s*=\s*(.+)");
                if (defaultMatch.Success)
                {
                    namePart = defaultMatch.Groups[1].Value;
                    defaultValue = defaultMatch.Groups[2].Value;
                    hasDefault = true;
                }

                parameters.Add(new FunctionParameter
                {
                    Name = namePart.TrimStart('@'),
                    Type = typePart,
                    HasDefault = hasDefault,
                    DefaultValue = defaultValue,
                    IsFromBody = isFromBody,
                    IsFromQuery = isFromQuery,
                    IsFromRoute = isFromRoute
                });
            }
        }

        return parameters;
    }

    private static string? ExtractXmlDocSummary(string[] lines, int currentLine)
    {
        for (var j = currentLine - 1; j >= 0 && j >= currentLine - 10; j--)
        {
            var match = Regex.Match(lines[j], @"///\s*<summary>\s*(?<summary>[^<]+)\s*</summary>");
            if (match.Success) return match.Groups["summary"].Value.Trim();
        }
        return null;
    }

    private static string? ExtractXmlDocReturns(string[] lines, int currentLine)
    {
        for (var j = currentLine - 1; j >= 0 && j >= currentLine - 10; j--)
        {
            var match = Regex.Match(lines[j], @"///\s*<returns>(?<returns>[^<]+)</returns>");
            if (match.Success) return match.Groups["returns"].Value.Trim();
        }
        return null;
    }

    private static string? ExtractHttpAttribute(string[] lines, int currentLine)
    {
        for (var j = currentLine - 1; j >= 0 && j >= currentLine - 5; j--)
        {
            var match = Regex.Match(lines[j], @"\[Http(Get|Post|Put|Delete|Patch|Head|Options)\]");
            if (match.Success) return match.Groups[1].Value.ToUpperInvariant();
        }
        return null;
    }

    private static string? ExtractRouteAttribute(string line)
    {
        var match = Regex.Match(line, @"\[Route\((?:\""(?<route>[^\""]+)\""|'(?<route>[^']+)')\)\]");
        return match.Success ? match.Groups["route"].Value : null;
    }

    private static bool DetectController(string content, string language)
    {
        if (language == "csharp")
            return Regex.IsMatch(content, @":\s*ControllerBase|:\s*Controller|\[ApiController\]");
        if (language is "typescript" or "javascript")
            return Regex.IsMatch(content, @"@Controller|@RestController|Router\(|router\.(get|post|put|delete|patch)\(");
        if (language == "python")
            return Regex.IsMatch(content, @"from\s+(flask|fastapi|django)|@app\.route|@router\.(get|post|put|delete)");
        if (language == "java")
            return Regex.IsMatch(content, @"@RestController|@Controller");
        return false;
    }

    private static string? ExtractRoutePrefix(string content, string language)
    {
        if (language == "csharp")
        {
            var match = Regex.Match(content, @"\[Route\((?:\""(?<route>api/[^\""]+)\""|'(?<route>api/[^']+)')\)\]");
            return match.Success ? match.Groups["route"].Value : null;
        }
        if (language is "typescript" or "javascript")
        {
            var match = Regex.Match(content, @"Router\(\{[^}]*prefix:\s*['""](?<route>[^'""]+)");
            return match.Success ? match.Groups["route"].Value : null;
        }
        if (language == "python")
        {
            var match = Regex.Match(content, @"@\w+\.route\(['""](?<route>[^'""]+)");
            return match.Success ? match.Groups["route"].Value : null;
        }
        return null;
    }

    private static string? ExtractClassName(string content, string language)
    {
        if (language == "csharp")
        {
            var match = Regex.Match(content, @"(class|record)\s+(\w+)");
            return match.Success ? match.Groups[2].Value : null;
        }
        if (language is "typescript" or "javascript")
        {
            var match = Regex.Match(content, @"(class|export\s+class)\s+(\w+)");
            return match.Success ? match.Groups[2].Value : null;
        }
        if (language == "python")
        {
            var match = Regex.Match(content, @"class\s+(\w+)");
            return match.Success ? match.Groups[1].Value : null;
        }
        if (language == "java")
        {
            var match = Regex.Match(content, @"(public\s+)?class\s+(\w+)");
            return match.Success ? match.Groups[2].Value : null;
        }
        return null;
    }

    private static string? ExtractNamespace(string content, string language)
    {
        if (language == "csharp")
        {
            var match = Regex.Match(content, @"namespace\s+([\w.]+)");
            return match.Success ? match.Groups[1].Value : null;
        }
        if (language is "typescript" or "javascript")
        {
            var match = Regex.Match(content, @"(module|namespace)\s+(\w+(?:\.\w+)*)");
            return match.Success ? match.Groups[2].Value : null;
        }
        if (language == "java")
        {
            var match = Regex.Match(content, @"package\s+([\w.]+)");
            return match.Success ? match.Groups[1].Value : null;
        }
        return null;
    }

    private static bool DetectExpressRoute(string[] lines, int currentLine)
    {
        for (var j = Math.Max(0, currentLine - 3); j <= Math.Min(lines.Length - 1, currentLine + 3); j++)
        {
            if (Regex.IsMatch(lines[j], @"router\.(get|post|put|delete|patch)\(|app\.(get|post|put|delete|patch)\("))
                return true;
        }
        return false;
    }

    private static string? ExtractExpressRoute(string line)
    {
        var match = Regex.Match(line, @"(?:router|app)\.(?:get|post|put|delete|patch)\(['""](?<route>[^'""]+)");
        return match.Success ? match.Groups["route"].Value : null;
    }

    private static bool DetectFlaskDjangoRoute(string[] lines, int currentLine)
    {
        for (var j = Math.Max(0, currentLine - 5); j <= currentLine; j++)
        {
            if (Regex.IsMatch(lines[j], @"@\w+\.route\(|@\w+\.(get|post|put|delete|patch)\("))
                return true;
        }
        return false;
    }

    private static string? ExtractFlaskDjangoRoute(string line)
    {
        var match = Regex.Match(line, @"@\w+\.route\(['""](?<route>[^'""]+)");
        return match.Success ? match.Groups["route"].Value : null;
    }

    private static List<string> ExtractJavaAnnotations(string[] lines, int currentLine)
    {
        var annotations = new List<string>();
        for (var j = currentLine - 1; j >= 0 && j >= currentLine - 5; j--)
        {
            var trimmed = lines[j].Trim();
            if (trimmed.StartsWith("@"))
                annotations.Add(trimmed.Split('(', ')')[0].Trim());
            else
                break;
        }
        return annotations;
    }

    private static string? ExtractJavaHttpMethod(List<string> annotations)
    {
        foreach (var ann in annotations)
        {
            var match = Regex.Match(ann, @"@(GetMapping|PostMapping|PutMapping|DeleteMapping|PatchMapping|RequestMapping)");
            if (match.Success)
                return match.Groups[1].Value switch
                {
                    "GetMapping" => "GET",
                    "PostMapping" => "POST",
                    "PutMapping" => "PUT",
                    "DeleteMapping" => "DELETE",
                    "PatchMapping" => "PATCH",
                    "RequestMapping" => null,
                    _ => null
                };
        }
        return null;
    }

    private static string? ExtractJavaRequestMapping(List<string> annotations)
    {
        foreach (var ann in annotations)
        {
            var match = Regex.Match(ann, @"@(?:GetMapping|PostMapping|PutMapping|DeleteMapping|PatchMapping|RequestMapping)\(\s*(?:value\s*=\s*)?['""](?<route>[^'""]+)");
            if (match.Success)
                return match.Groups["route"].Value;
        }
        return null;
    }
}
