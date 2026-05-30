using System.Text;

namespace _036_AI_Pair_Programmer.Services;

public sealed class CodeChunkerService
{
    private static readonly HashSet<string> DefaultExtensions =
    [
        ".cs", ".razor", ".cshtml", ".json", ".md", ".yml", ".yaml", ".xml",
        ".ts", ".tsx", ".js", ".jsx", ".py", ".go", ".java", ".sql", ".sh"
    ];

    private static readonly HashSet<string> SkipDirs =
    ["bin", "obj", ".git", "node_modules", ".idea", ".vscode"];

    public IReadOnlyList<RawCodeChunk> BuildChunks(string repoPath, IEnumerable<string> languages, int maxChunkTokens, int maxFiles)
    {
        var exts = ResolveExtensions(languages);
        var files = Directory.EnumerateFiles(repoPath, "*", SearchOption.AllDirectories)
            .Where(path => !IsInSkippedDirectory(path, repoPath))
            .Where(path => exts.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Take(Math.Clamp(maxFiles, 1, 2_000))
            .ToList();

        var chunks = new List<RawCodeChunk>();
        var targetChars = Math.Clamp(maxChunkTokens, 200, 2_000) * 4;

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            var relativePath = Path.GetRelativePath(repoPath, file).Replace('\\', '/');

            var buffer = new StringBuilder();
            var startLine = 1;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                buffer.AppendLine(line);

                if (buffer.Length >= targetChars || i == lines.Length - 1)
                {
                    var endLine = i + 1;
                    var text = buffer.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        chunks.Add(new RawCodeChunk(relativePath, text, startLine, endLine));
                    }

                    buffer.Clear();
                    startLine = i + 2;
                }
            }
        }

        return chunks;
    }

    private static HashSet<string> ResolveExtensions(IEnumerable<string> languages)
    {
        var list = languages?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToLowerInvariant()).ToList() ?? new();
        if (list.Count == 0)
        {
            return new HashSet<string>(DefaultExtensions, StringComparer.OrdinalIgnoreCase);
        }

        var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lang in list)
        {
            switch (lang)
            {
                case "csharp":
                case "cs":
                    mapped.Add(".cs");
                    mapped.Add(".razor");
                    break;
                case "typescript":
                case "ts":
                    mapped.Add(".ts");
                    mapped.Add(".tsx");
                    break;
                case "javascript":
                case "js":
                    mapped.Add(".js");
                    mapped.Add(".jsx");
                    break;
                case "python":
                case "py":
                    mapped.Add(".py");
                    break;
                case "markdown":
                case "md":
                    mapped.Add(".md");
                    break;
                default:
                    mapped.Add($".{lang.TrimStart('.')}" );
                    break;
            }
        }

        return mapped.Count > 0 ? mapped : new HashSet<string>(DefaultExtensions, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsInSkippedDirectory(string path, string repoPath)
    {
        var relative = Path.GetRelativePath(repoPath, path);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => SkipDirs.Contains(p));
    }
}

public sealed record RawCodeChunk(
    string FilePath,
    string Text,
    int StartLine,
    int EndLine);
