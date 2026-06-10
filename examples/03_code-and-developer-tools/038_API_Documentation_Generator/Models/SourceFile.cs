namespace _038_API_Documentation_Generator.Models;

public sealed class SourceFile
{
    public string FilePath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<ApiFunction> Functions { get; set; } = new();
    public bool IsController { get; set; }
    public string? RoutePrefix { get; set; }
    public string? ClassName { get; set; }
    public string? Namespace { get; set; }
}
