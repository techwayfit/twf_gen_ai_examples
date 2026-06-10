using System.Text.Json;

namespace _038_API_Documentation_Generator.Models;

public sealed class ApiDocumentationResult
{
    public string ApiTitle { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public int EndpointsDocumented { get; set; }
    public int FilesScanned { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public JsonElement? OpenApiSpec { get; set; }
    public string MarkdownDocumentation { get; set; } = string.Empty;
    public List<GeneratedEndpointDoc> Endpoints { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public TimeSpan Duration { get; set; }
}
