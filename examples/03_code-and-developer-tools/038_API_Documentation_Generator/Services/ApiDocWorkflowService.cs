using System.Diagnostics;
using System.Text;
using System.Text.Json;
using _038_API_Documentation_Generator.Models;
using TwfAiFramework;
using TwfAiFramework.Nodes;

namespace _038_API_Documentation_Generator.Services;

public sealed class ApiDocWorkflowService(
    ILogger<ApiDocWorkflowService> logger,
    CodebaseScannerService scannerService,
    LlmService llmService,
    IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions LenientJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task<ApiDocumentationResult> GenerateAsync(ScanRequest request, string apiKey, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var result = new ApiDocumentationResult
        {
            ApiTitle = request.ApiTitle,
            ApiVersion = request.ApiVersion,
            BaseUrl = request.BaseUrl,
            GeneratedAt = DateTime.UtcNow
        };

        logger.LogInformation("Starting API documentation generation for {RepoPath}", request.RepoPath);

        // Step 1: Scan codebase for source files
        var sourceFiles = scannerService.ScanCodebase(request.RepoPath, request.Languages, request.MaxFiles);
        result.FilesScanned = sourceFiles.Count;

        logger.LogInformation("Scanned {Count} source files with API functions", sourceFiles.Count);

        if (sourceFiles.Count == 0)
        {
            result.Warnings.Add(Constants.Messages.NoApiFunctionsFound);
            result.Duration = sw.Elapsed;
            return result;
        }

        // Step 2: For each source file, extract endpoint docs using ForEach pattern
        var chatModel = configuration["OpenAI:ChatModel"] ?? "gpt-4o-mini";
        var endpoint = configuration["OpenAI:Endpoint"] ?? "https://api.openai.com/v1";

        // Use Workflow.ForEach pattern to process functions across files
        var allEndpoints = await ProcessAllFunctionsAsync(
            sourceFiles, apiKey, chatModel, endpoint, ct);

        result.Endpoints = allEndpoints;
        result.EndpointsDocumented = allEndpoints.Count;

        logger.LogInformation("Generated documentation for {Count} endpoints", allEndpoints.Count);

        // Step 3: Assemble OpenAPI spec using MergeNode pattern
        if (request.GenerateOpenApiSpec && allEndpoints.Count > 0)
        {
            result.OpenApiSpec = await AssembleOpenApiSpecAsync(
                allEndpoints, request, apiKey, chatModel, endpoint, ct);
        }

        // Step 4: Generate Markdown documentation
        if (request.GenerateMarkdown && allEndpoints.Count > 0)
        {
            result.MarkdownDocumentation = GenerateMarkdownDocumentation(
                allEndpoints, request, sourceFiles);
        }

        result.Duration = sw.Elapsed;
        logger.LogInformation("API documentation generation completed in {Duration}", result.Duration);

        return result;
    }

    private async Task<List<GeneratedEndpointDoc>> ProcessAllFunctionsAsync(
        List<SourceFile> sourceFiles,
        string apiKey,
        string chatModel,
        string endpoint,
        CancellationToken ct)
    {
        var allDocs = new List<GeneratedEndpointDoc>();
        var semaphore = new SemaphoreSlim(3); // Limit concurrency

        var tasks = new List<Task>();

        // Workflow.ForEach() pattern — process each source file's functions in parallel with rate limiting
        foreach (var sourceFile in sourceFiles)
        {
            foreach (var func in sourceFile.Functions)
            {
                var capturedFile = sourceFile;
                var capturedFunc = func;

                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        ct.ThrowIfCancellationRequested();
                        var doc = await GenerateEndpointDocAsync(
                            capturedFile, capturedFunc, apiKey, chatModel, endpoint, ct);

                        if (doc != null)
                        {
                            lock (allDocs)
                            {
                                allDocs.Add(doc);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Cancellation is expected
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to generate doc for {File}:{Func}",
                            capturedFile.RelativePath, capturedFunc.Name);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, ct));
            }
        }

        await Task.WhenAll(tasks);
        return allDocs;
    }

    private async Task<GeneratedEndpointDoc?> GenerateEndpointDocAsync(
        SourceFile sourceFile,
        ApiFunction func,
        string apiKey,
        string chatModel,
        string endpoint,
        CancellationToken ct)
    {
        var userPrompt = Constants.Prompts.DocGenerationUserPrompt
            .Replace("{{file_path}}", sourceFile.RelativePath)
            .Replace("{{language}}", sourceFile.Language)
            .Replace("{{visibility}}", func.Visibility)
            .Replace("{{declaration}}", func.Declaration)
            .Replace("{{xml_doc}}", func.XmlDocSummary ?? "(none)")
            .Replace("{{class_name}}", sourceFile.ClassName ?? "(none)")
            .Replace("{{route_prefix}}", sourceFile.RoutePrefix ?? "(none)");

        var response = await llmService.ChatAsync(
            Constants.Prompts.DocGenerationSystemPrompt,
            userPrompt,
            apiKey,
            chatModel,
            endpoint,
            4096,
            ct);

        return ParseEndpointDoc(response, sourceFile.RelativePath);
    }

    private static GeneratedEndpointDoc? ParseEndpointDoc(string json, string sourceFile)
    {
        try
        {
            // Extract JSON from response (handle markdown code fences)
            var jsonStart = json.IndexOf('{');
            var jsonEnd = json.LastIndexOf('}');
            if (jsonStart == -1 || jsonEnd == -1) return null;

            var cleaned = json[jsonStart..(jsonEnd + 1)];
            var doc = JsonSerializer.Deserialize<GeneratedEndpointDoc>(cleaned, LenientJsonOpts);

            if (doc != null)
                doc.SourceFile = sourceFile;

            return doc;
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"Failed to parse endpoint doc: {ex.Message}");
            return null;
        }
    }

    private async Task<JsonElement> AssembleOpenApiSpecAsync(
        List<GeneratedEndpointDoc> endpoints,
        ScanRequest request,
        string apiKey,
        string chatModel,
        string endpoint,
        CancellationToken ct)
    {
        var endpointEntries = JsonSerializer.Serialize(endpoints, JsonOpts);

        var userPrompt = Constants.Prompts.SpecAssemblyPrompt
            .Replace("{{api_title}}", request.ApiTitle)
            .Replace("{{api_version}}", request.ApiVersion)
            .Replace("{{base_url}}", request.BaseUrl)
            .Replace("{{endpoint_entries}}", endpointEntries);

        var response = await llmService.ChatAsync(
            "You are an OpenAPI 3.1 specification compiler. Output ONLY valid JSON.",
            userPrompt,
            apiKey,
            chatModel,
            endpoint,
            8192,
            ct);

        var jsonStart = response.IndexOf('{');
        var jsonEnd = response.LastIndexOf('}');

        if (jsonStart == -1 || jsonEnd == -1)
            return JsonSerializer.SerializeToElement(new { error = "Failed to assemble OpenAPI spec" });

        var cleaned = response[jsonStart..(jsonEnd + 1)];
        return JsonSerializer.Deserialize<JsonElement>(cleaned, LenientJsonOpts);
    }

    private static string GenerateMarkdownDocumentation(
        List<GeneratedEndpointDoc> endpoints,
        ScanRequest request,
        List<SourceFile> sourceFiles)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# {request.ApiTitle}");
        sb.AppendLine();
        sb.AppendLine($"**API Version:** {request.ApiVersion}");
        sb.AppendLine($"**Base URL:** `{request.BaseUrl}`");
        sb.AppendLine($"**Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}");
        sb.AppendLine($"**Endpoints:** {endpoints.Count}");
        sb.AppendLine();

        // Table of contents
        sb.AppendLine("## Table of Contents");
        sb.AppendLine();

        var grouped = endpoints.GroupBy(e =>
        {
            var parts = e.Path.Trim('/').Split('/');
            return parts.Length > 0 ? parts[0] : "general";
        });

        foreach (var group in grouped)
        {
            sb.AppendLine($"- [{group.Key}](#{group.Key.ToLowerInvariant()})");
        }

        sb.AppendLine();

        // Source files overview
        sb.AppendLine("## Source Files Analyzed");
        sb.AppendLine();
        sb.AppendLine("| File | Language | Endpoints |");
        sb.AppendLine("|------|----------|-----------|");

        foreach (var file in sourceFiles.OrderBy(f => f.RelativePath))
        {
            var count = endpoints.Count(e =>
                e.SourceFile.Equals(file.RelativePath, StringComparison.OrdinalIgnoreCase));
            sb.AppendLine($"| `{file.RelativePath}` | {file.Language} | {count} |");
        }

        sb.AppendLine();

        // Endpoint details
        sb.AppendLine("## Endpoints");
        sb.AppendLine();

        foreach (var group in grouped)
        {
            var anchor = group.Key.ToLowerInvariant();
            sb.AppendLine($"### {group.Key}");
            sb.AppendLine();

            foreach (var endpoint in group.OrderBy(e => e.HttpMethod).ThenBy(e => e.Path))
            {
                sb.AppendLine($"#### {endpoint.HttpMethod} `{endpoint.Path}`");
                sb.AppendLine();
                sb.AppendLine($"**Operation ID:** `{endpoint.OperationId}`");
                sb.AppendLine();
                sb.AppendLine($"**Source:** `{endpoint.SourceFile}`");
                sb.AppendLine();

                if (!string.IsNullOrWhiteSpace(endpoint.Summary))
                {
                    sb.AppendLine(endpoint.Summary);
                    sb.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(endpoint.Description))
                {
                    sb.AppendLine(endpoint.Description);
                    sb.AppendLine();
                }

                if (endpoint.Parameters.Count > 0)
                {
                    sb.AppendLine("**Parameters:**");
                    sb.AppendLine();
                    sb.AppendLine("| Name | In | Required | Description |");
                    sb.AppendLine("|------|----|----------|-------------|");

                    foreach (var param in endpoint.Parameters)
                    {
                        sb.AppendLine($"| `{param.Name}` | {param.In} | {param.Required} | {param.Description} |");
                    }

                    sb.AppendLine();
                }

                if (endpoint.UsageExamples.Count > 0)
                {
                    sb.AppendLine("**Usage Examples:**");
                    sb.AppendLine();

                    foreach (var (lang, code) in endpoint.UsageExamples)
                    {
                        if (!string.IsNullOrWhiteSpace(code))
                        {
                            sb.AppendLine($"```{lang}");
                            sb.AppendLine(code);
                            sb.AppendLine("```");
                            sb.AppendLine();
                        }
                    }
                }
            }
        }

        return sb.ToString();
    }
}
