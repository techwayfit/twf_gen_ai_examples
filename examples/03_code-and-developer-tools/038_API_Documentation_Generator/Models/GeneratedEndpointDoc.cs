using System.Text.Json;

namespace _038_API_Documentation_Generator.Models;

public sealed class GeneratedEndpointDoc
{
    public string Path { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = "GET";
    public string OperationId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public List<OpenApiParameter> Parameters { get; set; } = new();
    public JsonElement? RequestBody { get; set; }
    public Dictionary<string, OpenApiResponse> Responses { get; set; } = new();
    public string MarkdownDoc { get; set; } = string.Empty;
    public Dictionary<string, string> UsageExamples { get; set; } = new();
    public string SourceFile { get; set; } = string.Empty;
}

public sealed class OpenApiParameter
{
    public string Name { get; set; } = string.Empty;
    public string In { get; set; } = "query";
    public bool Required { get; set; }
    public string Description { get; set; } = string.Empty;
    public JsonElement? Schema { get; set; }
}

public sealed class OpenApiResponse
{
    public string Description { get; set; } = string.Empty;
    public JsonElement? Content { get; set; }
}
