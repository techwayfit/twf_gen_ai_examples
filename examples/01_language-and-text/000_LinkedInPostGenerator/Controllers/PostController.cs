using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TwfAiFramework.Nodes.AI;
using _000_LinkedInPostGenerator.Services;

namespace _000_LinkedInPostGenerator.Controllers;

[ApiController]
[Route("api/[controller]")]
[IgnoreAntiforgeryToken]
public class PostController : ControllerBase
{
    private readonly ILogger<PostController> _logger;
    private readonly IConfiguration          _configuration;
    private readonly PostWorkflowService     _workflowService;
    private readonly ProfileService          _profileService;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public PostController(
        ILogger<PostController> logger,
        IConfiguration          configuration,
        PostWorkflowService     workflowService,
        ProfileService          profileService)
    {
        _logger          = logger;
        _configuration   = configuration;
        _workflowService = workflowService;
        _profileService  = profileService;
    }

    /// <summary>
    /// Generates a LinkedIn post and streams progress as SSE events.
    ///
    /// Event types emitted:
    ///   stage    — { message, stageIndex, totalStages }   pipeline progress
    ///   complete — GeneratedPost                          full post with metadata
    ///   error    — { error }                              terminal error
    /// </summary>
    [HttpPost("generate")]
    public async Task Generate([FromBody] PostRequest request, CancellationToken ct)
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
        if (string.IsNullOrWhiteSpace(request.Topic))
        {
            await SendAsync("error", new { error = Constants.Messages.EmptyTopic });
            return;
        }

        if (string.IsNullOrWhiteSpace(request.CurrentRole))
        {
            await SendAsync("error", new { error = Constants.Messages.EmptyRole });
            return;
        }

        if (request.Topic.Length > 2_000)
        {
            await SendAsync("error", new { error = Constants.Messages.TopicTooLong });
            return;
        }

        var maxChars = request.MaxChars is > 0 and <= 3_000 ? request.MaxChars : 1300;
        var llmConfig = BuildLlmConfig(openAiKey);

        var brief = new PostBrief
        {
            Topic             = request.Topic.Trim(),
            CurrentRole       = request.CurrentRole,
            TargetAudience    = request.TargetAudience,
            MaxChars          = maxChars,
            RelatedLinks      = request.RelatedLinks ?? new List<string>(),
            AdditionalContext = request.AdditionalContext ?? string.Empty
        };

        try
        {
            var result = await _workflowService.RunAsync(
                brief:             brief,
                sendStageAsync:    stage => SendAsync("stage",    stage),
                sendCompleteAsync: post  => SendAsync("complete", post),
                llmConfig:         llmConfig,
                ct:                ct);

            if (!result.IsSuccess)
            {
                _logger.LogError("Post generation workflow failed: {Error}", result.ErrorMessage);
                await SendAsync("error", new { error = result.ErrorMessage ?? Constants.Messages.WorkflowFailed });
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during post generation");
            try
            {
                await SendAsync("error", new { error = Constants.Messages.UnexpectedError });
            }
            catch { /* response may be gone */ }
        }
    }

    /// <summary>Returns the current author profile.</summary>
    [HttpGet("profile")]
    public IActionResult GetProfile()
    {
        var profile = _profileService.Load();
        return Ok(profile);
    }

    /// <summary>Saves an updated author profile.</summary>
    [HttpPut("profile")]
    public IActionResult SaveProfile([FromBody] AuthorProfile profile)
    {
        _profileService.Save(profile);
        return Ok(new { saved = true });
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

public class PostRequest
{
    public string       Topic             { get; set; } = string.Empty;
    public string       CurrentRole       { get; set; } = "developer";
    public string       TargetAudience    { get; set; } = "engineers";
    public int          MaxChars          { get; set; } = 1300;
    public List<string> RelatedLinks      { get; set; } = new();
    public string       AdditionalContext { get; set; } = string.Empty;
}
