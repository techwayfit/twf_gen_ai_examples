namespace _038_API_Documentation_Generator.Models;

public sealed class ScanRequest
{
    public string RepoPath { get; set; } = string.Empty;
    public List<string> Languages { get; set; } = new();
    public int MaxFiles { get; set; } = 500;
    public string ApiTitle { get; set; } = "API Documentation";
    public string ApiVersion { get; set; } = "1.0.0";
    public string BaseUrl { get; set; } = "https://api.example.com";
    public bool GenerateMarkdown { get; set; } = true;
    public bool GenerateOpenApiSpec { get; set; } = true;
}
