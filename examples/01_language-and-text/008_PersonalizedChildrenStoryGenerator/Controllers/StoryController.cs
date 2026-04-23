using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TwfAiFramework.Nodes.AI;
using _008_PersonalizedChildrenStoryGenerator.Services;

namespace _008_PersonalizedChildrenStoryGenerator.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StoryController : ControllerBase
{
    private readonly ILogger<StoryController> _logger;
    private readonly IConfiguration           _configuration;
    private readonly StoryWorkflowService     _workflowService;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public StoryController(
        ILogger<StoryController> logger,
        IConfiguration           configuration,
        StoryWorkflowService     workflowService)
    {
        _logger          = logger;
        _configuration   = configuration;
        _workflowService = workflowService;
    }

    /// <summary>
    /// Accepts a story generation request and streams pipeline progress back as SSE events.
    ///
    /// Event types emitted:
    ///   stage    — { message, stageIndex, totalStages }   pipeline progress
    ///   complete — StoryResult                             full story payload
    ///   error    — { error }                               terminal error
    /// </summary>
    [HttpPost("generate")]
    public async Task Generate([FromBody] StoryRequest request, CancellationToken ct)
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
        if (string.IsNullOrWhiteSpace(request.ChildName))
        {
            await SendAsync("error", new { error = Constants.Messages.EmptyChildName });
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Interest))
        {
            await SendAsync("error", new { error = Constants.Messages.EmptyInterest });
            return;
        }

        if (string.IsNullOrWhiteSpace(request.MoralLesson))
        {
            await SendAsync("error", new { error = Constants.Messages.EmptyMoralLesson });
            return;
        }

        if (request.ChildName.Length > 50)
        {
            await SendAsync("error", new { error = Constants.Messages.ChildNameTooLong });
            return;
        }

        if (request.Interest.Length > 100)
        {
            await SendAsync("error", new { error = Constants.Messages.InterestTooLong });
            return;
        }

        if (request.MoralLesson.Length > 200)
        {
            await SendAsync("error", new { error = Constants.Messages.MoralLessonTooLong });
            return;
        }

        var llmConfig = BuildLlmConfig(openAiKey);

        var brief = new StoryBrief
        {
            ChildName   = request.ChildName.Trim(),
            Interest    = request.Interest.Trim(),
            MoralLesson = request.MoralLesson.Trim(),
            AgeRangeMin = request.AgeRangeMin,
            Language    = string.IsNullOrWhiteSpace(request.Language)
                              ? "English"
                              : request.Language.Trim(),
            StoryLength = string.IsNullOrWhiteSpace(request.StoryLength)
                              ? "short"
                              : request.StoryLength.Trim()
        };

        try
        {
            var result = await _workflowService.RunAsync(
                brief:             brief,
                sendStageAsync:    stage  => SendAsync("stage",    stage),
                sendCompleteAsync: story  => SendAsync("complete", story),
                llmConfig:         llmConfig,
                ct:                ct);

            if (!result.IsSuccess)
            {
                _logger.LogError("Story workflow failed: {Error}", result.ErrorMessage);
                await SendAsync("error", new { error = result.ErrorMessage ?? Constants.Messages.WorkflowFailed });
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during story generation");
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

public class StoryRequest
{
    public string ChildName   { get; set; } = string.Empty;
    public string Interest    { get; set; } = string.Empty;
    public string MoralLesson { get; set; } = string.Empty;
    public int    AgeRangeMin { get; set; } = 6;
    public string Language    { get; set; } = "English";
    public string StoryLength { get; set; } = "short";
}
