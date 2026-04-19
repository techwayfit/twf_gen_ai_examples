using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TwfAiFramework.Nodes.AI;
using _007_FakeNewsMisInformationDetector.Services;

namespace _007_FakeNewsMisInformationDetector.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FactCheckController : ControllerBase
{
    private readonly ILogger<FactCheckController> _logger;
    private readonly IConfiguration               _configuration;
    private readonly FactCheckWorkflowService     _workflowService;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public FactCheckController(
        ILogger<FactCheckController> logger,
        IConfiguration               configuration,
        FactCheckWorkflowService     workflowService)
    {
        _logger          = logger;
        _configuration   = configuration;
        _workflowService = workflowService;
    }

    /// <summary>
    /// Accepts an article text and streams the fact-check pipeline back as SSE events.
    ///
    /// Event types emitted:
    ///   stage    — { message, stageIndex, totalStages }   pipeline progress
    ///   complete — CredibilityReport                       full structured report
    ///   error    — { error }                               terminal error
    /// </summary>
    [HttpPost("analyse")]
    public async Task Analyse([FromBody] FactCheckRequest request, CancellationToken ct)
    {
        Response.Headers["Content-Type"]      = "text/event-stream";
        Response.Headers["Cache-Control"]     = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        async Task SendAsync(string evt, object data)
        {
            var json = JsonSerializer.Serialize(data, JsonOpts);
            await Response.WriteAsync($"event: {evt}\ndata: {json}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        // Validate configuration
        var openAiKey = _configuration["OpenAI:ApiKey"];
        if (string.IsNullOrEmpty(openAiKey) || openAiKey == "your-openai-api-key-here")
        {
            await SendAsync("error", new { error = Constants.Messages.OpenAiKeyNotConfigured });
            return;
        }

        var searchKey = _configuration["Search:ApiKey"];
        if (string.IsNullOrEmpty(searchKey) || searchKey == "your-serper-api-key-here")
        {
            await SendAsync("error", new { error = Constants.Messages.SearchApiKeyNotConfigured });
            return;
        }

        // Validate input
        if (string.IsNullOrWhiteSpace(request.ArticleText))
        {
            await SendAsync("error", new { error = Constants.Messages.EmptyArticle });
            return;
        }

        if (request.ArticleText.Length > 10_000)
        {
            await SendAsync("error", new { error = Constants.Messages.ArticleTooLong });
            return;
        }

        var searchEndpoint = _configuration["Search:Endpoint"] ?? "https://google.serper.dev/search";
        var maxClaims      = request.MaxClaims is > 0 and <= 10 ? request.MaxClaims : 5;
        var llmConfig      = BuildLlmConfig(openAiKey);

        var brief = new FactCheckBrief
        {
            ArticleText = request.ArticleText.Trim(),
            MaxClaims   = maxClaims
        };

        try
        {
            var result = await _workflowService.RunAsync(
                brief:             brief,
                sendStageAsync:    stage  => SendAsync("stage",    stage),
                sendCompleteAsync: report => SendAsync("complete", report),
                llmConfig:         llmConfig,
                searchApiKey:      searchKey,
                searchEndpoint:    searchEndpoint,
                ct:                ct);

            if (!result.IsSuccess)
            {
                _logger.LogError("Fact-check workflow failed: {Error}", result.ErrorMessage);
                await SendAsync("error", new { error = result.ErrorMessage ?? Constants.Messages.WorkflowFailed });
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during fact-check");
            try
            {
                await SendAsync("error", new { error = Constants.Messages.UnexpectedError });
            }
            catch { /* response may be gone */ }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private LlmConfig BuildLlmConfig(string apiKey)
    {
        var model    = _configuration["OpenAI:Model"]    ?? LlmConfig.OpenAI(apiKey).Model;
        var endpoint = _configuration["OpenAI:Endpoint"] ?? LlmConfig.OpenAI(apiKey).ApiEndpoint;
        return LlmConfig.LmServer(model: model, apiKey: apiKey, apiEndpoint: endpoint);
    }
}

// ── Request model ─────────────────────────────────────────────────────────────

public class FactCheckRequest
{
    public string ArticleText { get; set; } = string.Empty;
    public int    MaxClaims   { get; set; } = 5;
}
