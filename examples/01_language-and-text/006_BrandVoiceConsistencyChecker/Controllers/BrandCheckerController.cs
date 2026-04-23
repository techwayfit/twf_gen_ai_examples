using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TwfAiFramework.Nodes.AI;
using _006_BrandVoiceConsistencyChecker.Services;

namespace _006_BrandVoiceConsistencyChecker.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BrandCheckerController : ControllerBase
{
    private readonly ILogger<BrandCheckerController> _logger;
    private readonly IConfiguration                  _configuration;
    private readonly BrandWorkflowService            _workflowService;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public BrandCheckerController(
        ILogger<BrandCheckerController> logger,
        IConfiguration                  configuration,
        BrandWorkflowService            workflowService)
    {
        _logger          = logger;
        _configuration   = configuration;
        _workflowService = workflowService;
    }

    /// <summary>
    /// Accepts a brand check request and streams the pipeline back as SSE events.
    ///
    /// Event types emitted:
    ///   stage    — { message, stageIndex, totalStages }   pipeline progress
    ///   complete — ConsistencyReport                       full structured report
    ///   error    — { error }                               terminal error
    /// </summary>
    [HttpPost("analyse")]
    public async Task Analyse([FromBody] BrandCheckRequest request, CancellationToken ct)
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

        // Validate input
        if (string.IsNullOrWhiteSpace(request.CopyText))
        {
            await SendAsync("error", new { error = Constants.Messages.EmptyCopyText });
            return;
        }

        if (string.IsNullOrWhiteSpace(request.BrandGuidelines))
        {
            await SendAsync("error", new { error = Constants.Messages.EmptyBrandGuidelines });
            return;
        }

        if (request.CopyText.Length > 8_000)
        {
            await SendAsync("error", new { error = Constants.Messages.CopyTooLong });
            return;
        }

        if (request.BrandGuidelines.Length > 5_000)
        {
            await SendAsync("error", new { error = Constants.Messages.GuidelinesTooLong });
            return;
        }

        var embeddingModel = _configuration["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small";
        var llmConfig      = BuildLlmConfig(openAiKey);

        var brief = new BrandCheckBrief
        {
            CopyText        = request.CopyText.Trim(),
            BrandGuidelines = request.BrandGuidelines.Trim(),
            BrandName       = string.IsNullOrWhiteSpace(request.BrandName)
                                  ? "Our Brand"
                                  : request.BrandName.Trim(),
            CopyType        = string.IsNullOrWhiteSpace(request.CopyType)
                                  ? "marketing copy"
                                  : request.CopyType.Trim(),
            Strictness      = string.IsNullOrWhiteSpace(request.Strictness)
                                  ? "standard"
                                  : request.Strictness.Trim()
        };

        try
        {
            var result = await _workflowService.RunAsync(
                brief:             brief,
                sendStageAsync:    stage  => SendAsync("stage",    stage),
                sendCompleteAsync: report => SendAsync("complete", report),
                llmConfig:         llmConfig,
                openAiApiKey:      openAiKey,
                embeddingModel:    embeddingModel,
                ct:                ct);

            if (!result.IsSuccess)
            {
                _logger.LogError("Brand check workflow failed: {Error}", result.ErrorMessage);
                await SendAsync("error", new { error = result.ErrorMessage ?? Constants.Messages.WorkflowFailed });
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during brand check");
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

public class BrandCheckRequest
{
    public string CopyText        { get; set; } = string.Empty;
    public string BrandGuidelines { get; set; } = string.Empty;
    public string BrandName       { get; set; } = "Our Brand";
    public string CopyType        { get; set; } = "marketing copy";
    public string Strictness      { get; set; } = "standard";
}
