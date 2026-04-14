using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TwfAiFramework.Core;
using TwfAiFramework.Nodes.AI;
using _005_LongFormContentWriter.Services;

namespace _005_LongFormContentWriter.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ContentWriterController : ControllerBase
{
    private readonly ILogger<ContentWriterController> _logger;
    private readonly IConfiguration _configuration;
    private readonly ContentWorkflowService _workflowService;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ContentWriterController(
        ILogger<ContentWriterController> logger,
        IConfiguration configuration,
        ContentWorkflowService workflowService)
    {
        _logger          = logger;
        _configuration   = configuration;
        _workflowService = workflowService;
    }

    /// <summary>
    /// Accepts a content brief and streams the generation pipeline back as SSE events.
    ///
    /// Event types emitted:
    ///   stage    — { message, stageIndex, totalStages }          pipeline progress
    ///   outline  — { seoTitle, slug, outlineSections, faqIdeas } structured outline
    ///   chunk    — { delta }                                      streaming article text
    ///   complete — ContentPackage                                 full content package
    ///   error    — { error }                                      terminal error
    /// </summary>
    [HttpPost("generate")]
    public async Task Generate([FromBody] ContentBriefRequest request, CancellationToken ct)
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
        if (string.IsNullOrEmpty(searchKey) || searchKey == "your-serpapi-key-here")
        {
            await SendAsync("error", new { error = Constants.Messages.SearchApiKeyNotConfigured });
            return;
        }

        // Validate input
        if (string.IsNullOrWhiteSpace(request.PrimaryKeyword))
        {
            await SendAsync("error", new { error = Constants.Messages.EmptyKeyword });
            return;
        }

        if (request.PrimaryKeyword.Length > 200)
        {
            await SendAsync("error", new { error = Constants.Messages.KeywordTooLong });
            return;
        }

        var llmConfig = BuildLlmConfig(openAiKey);

        var brief = new ContentBrief
        {
            PrimaryKeyword    = request.PrimaryKeyword.Trim(),
            SecondaryKeywords = request.SecondaryKeywords?.Trim(),
            TargetAudience    = request.TargetAudience?.Trim(),
            SearchIntent      = request.SearchIntent?.Trim(),
            Tone              = request.Tone?.Trim(),
            TargetWordCount   = request.TargetWordCount is > 0 ? request.TargetWordCount : 1500,
            CallToAction      = request.CallToAction?.Trim()
        };

        // Sync write for streaming chunks from LlmNode (requires AllowSynchronousIO = true)
        void SendChunkSync(string chunk)
        {
            var bytes = Encoding.UTF8.GetBytes(
                $"event: chunk\ndata: {JsonSerializer.Serialize(new { delta = chunk }, JsonOpts)}\n\n");
            Response.Body.Write(bytes);
            Response.Body.Flush();
        }

        try
        {
            var result = await _workflowService.RunAsync(
                brief:             brief,
                sendStageAsync:    stage   => SendAsync("stage",    stage),
                sendOutlineAsync:  outline => SendAsync("outline",  outline),
                sendChunkSync:     SendChunkSync,
                sendCompleteAsync: pkg     => SendAsync("complete", pkg),
                llmConfig:         llmConfig,
                searchApiKey:      searchKey,
                ct:                ct);

            if (!result.IsSuccess)
            {
                _logger.LogError("Content workflow failed: {Error}", result.ErrorMessage);
                await SendAsync("error", new { error = result.ErrorMessage ?? Constants.Messages.WorkflowFailed });
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during content generation");
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

public class ContentBriefRequest
{
    public string  PrimaryKeyword    { get; set; } = string.Empty;
    public string? SecondaryKeywords  { get; set; }
    public string? TargetAudience    { get; set; }
    public string? SearchIntent      { get; set; }
    public string? Tone              { get; set; }
    public int     TargetWordCount   { get; set; } = 1500;
    public string? CallToAction      { get; set; }
}
