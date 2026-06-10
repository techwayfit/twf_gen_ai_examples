using _038_API_Documentation_Generator.Models;
using _038_API_Documentation_Generator.Services;
using Microsoft.AspNetCore.Mvc;

namespace _038_API_Documentation_Generator.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ApiDocsController(
    IConfiguration configuration,
    ApiDocWorkflowService workflowService) : ControllerBase
{
    [HttpPost("generate")]
    public async Task<ActionResult<ApiDocumentationResult>> Generate([FromBody] ScanRequest request, CancellationToken ct)
    {
        var apiKey = configuration["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "your-openai-api-key-here")
        {
            return BadRequest(new { error = Constants.Messages.OpenAiKeyNotConfigured });
        }

        if (string.IsNullOrWhiteSpace(request.RepoPath))
        {
            return BadRequest(new { error = Constants.Messages.RepoPathRequired });
        }

        var fullPath = Path.GetFullPath(request.RepoPath.Trim());
        if (!Directory.Exists(fullPath))
        {
            return NotFound(new { error = Constants.Messages.RepoPathNotFound });
        }

        var sanitized = new ScanRequest
        {
            RepoPath = fullPath,
            Languages = request.Languages,
            MaxFiles = Math.Clamp(request.MaxFiles, 1, 2_000),
            ApiTitle = string.IsNullOrWhiteSpace(request.ApiTitle) ? "API Documentation" : request.ApiTitle,
            ApiVersion = string.IsNullOrWhiteSpace(request.ApiVersion) ? "1.0.0" : request.ApiVersion,
            BaseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? "https://api.example.com" : request.BaseUrl,
            GenerateMarkdown = request.GenerateMarkdown,
            GenerateOpenApiSpec = request.GenerateOpenApiSpec
        };

        try
        {
            var result = await workflowService.GenerateAsync(sanitized, apiKey, ct);

            if (result.Endpoints.Count == 0)
            {
                return Ok(new
                {
                    warnings = new[] { Constants.Messages.NoApiFunctionsFound },
                    filesScanned = result.FilesScanned,
                    duration = result.Duration.ToString()
                });
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = Constants.Messages.DocGenerationFailed, detail = ex.Message });
        }
    }

    [HttpPost("scan")]
    public ActionResult<List<SourceFile>> ScanOnly([FromBody] ScanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RepoPath))
        {
            return BadRequest(new { error = Constants.Messages.RepoPathRequired });
        }

        var fullPath = Path.GetFullPath(request.RepoPath.Trim());
        if (!Directory.Exists(fullPath))
        {
            return NotFound(new { error = Constants.Messages.RepoPathNotFound });
        }

        var scanner = HttpContext.RequestServices.GetRequiredService<CodebaseScannerService>();
        var files = scanner.ScanCodebase(fullPath, request.Languages, Math.Clamp(request.MaxFiles, 1, 2_000));

        return Ok(new
        {
            filesScanned = files.Count,
            totalFunctions = files.Sum(f => f.Functions.Count),
            files = files.Select(f => new
            {
                path = f.RelativePath,
                language = f.Language,
                isController = f.IsController,
                className = f.ClassName,
                functions = f.Functions.Select(fn => new
                {
                    name = fn.Name,
                    visibility = fn.Visibility,
                    returnType = fn.ReturnType,
                    httpMethod = fn.HttpMethod,
                    route = fn.RouteTemplate,
                    isControllerAction = fn.IsControllerAction,
                    line = fn.LineNumber,
                    xmlDocSummary = fn.XmlDocSummary
                })
            })
        });
    }
}
