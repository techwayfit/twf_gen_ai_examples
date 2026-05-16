using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using _032_TextbookChapterQuestionGenerator.Services;

namespace _032_TextbookChapterQuestionGenerator.Controllers;

[ApiController]
[Route("api/[controller]")]
public class QuestionController : ControllerBase
{
    private readonly ILogger<QuestionController>          _logger;
    private readonly IConfiguration                       _configuration;
    private readonly QuestionGenerationWorkflowService    _workflowService;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public QuestionController(
        ILogger<QuestionController>       logger,
        IConfiguration                    configuration,
        QuestionGenerationWorkflowService workflowService)
    {
        _logger          = logger;
        _configuration   = configuration;
        _workflowService = workflowService;
    }

    // ── POST /api/Question/generate ───────────────────────────────────────────

    /// <summary>
    /// Accepts a chapter text and generation config, then streams the pipeline
    /// back as SSE events.
    ///
    /// Event types emitted:
    ///   stage    — { message, stageIndex, totalStages }    pipeline progress
    ///   complete — QuestionBankResult                      full structured result
    ///   error    — { error }                               terminal error
    /// </summary>
    [HttpPost("generate")]
    public async Task Generate([FromBody] GenerationApiRequest request, CancellationToken ct)
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

        var openAiKey = _configuration["OpenAI:ApiKey"];
        if (string.IsNullOrEmpty(openAiKey) || openAiKey == "your-openai-api-key-here")
        {
            await SendAsync("error", new { error = Constants.Messages.OpenAiKeyNotConfigured });
            return;
        }

        if (string.IsNullOrWhiteSpace(request.ChapterText))
        {
            await SendAsync("error", new { error = Constants.Messages.EmptyChapterText });
            return;
        }

        if (request.McqCount + request.ShortAnswerCount + request.EssayCount + request.TrueFalseCount + request.WordProblemCount <= 0)
        {
            await SendAsync("error", new { error = Constants.Messages.NoQuestionsRequested });
            return;
        }

        var llmModel    = _configuration["OpenAI:ChatModel"] ?? "gpt-4o-mini";
        var llmEndpoint = _configuration["OpenAI:Endpoint"]  ?? "https://api.openai.com/v1/chat/completions";

        var generationInput = new GenerationInput
        {
            ChapterText      = request.ChapterText.Trim(),
            Subject          = string.IsNullOrWhiteSpace(request.Subject) ? "General" : request.Subject.Trim(),
            McqCount         = Math.Clamp(request.McqCount,         0, 20),
            ShortAnswerCount = Math.Clamp(request.ShortAnswerCount,  0, 10),
            EssayCount       = Math.Clamp(request.EssayCount,        0, 5),
            TrueFalseCount   = Math.Clamp(request.TrueFalseCount,    0, 20),
            WordProblemCount = Math.Clamp(request.WordProblemCount,  0, 5),
            BloomLevels      = request.BloomLevels?.Count > 0
                ? request.BloomLevels
                : new() { "Remember", "Understand", "Apply" },
            Difficulty       = string.IsNullOrWhiteSpace(request.Difficulty) ? "mixed" : request.Difficulty,
        };

        try
        {
            var result = await _workflowService.RunAsync(
                request:           generationInput,
                sendStageAsync:    stage  => SendAsync("stage",    stage),
                sendCompleteAsync: result => SendAsync("complete", result),
                apiKey:            openAiKey,
                llmModel:          llmModel,
                llmEndpoint:       llmEndpoint,
                ct:                ct);

            if (!result.IsSuccess)
            {
                _logger.LogError("Question generation workflow failed: {Error}", result.ErrorMessage);
                await SendAsync("error",
                    new { error = result.ErrorMessage ?? Constants.Messages.WorkflowFailed });
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during question generation");
            try { await SendAsync("error", new { error = Constants.Messages.UnexpectedError }); }
            catch { /* response may be gone */ }
        }
    }
}

// ── Request model ─────────────────────────────────────────────────────────────

public class GenerationApiRequest
{
    public string       ChapterText      { get; set; } = string.Empty;
    public string       Subject          { get; set; } = "General";
    public int          McqCount         { get; set; } = 5;
    public int          ShortAnswerCount { get; set; } = 3;
    public int          EssayCount       { get; set; } = 1;
    public int          TrueFalseCount   { get; set; } = 5;
    public int          WordProblemCount { get; set; } = 0;
    public List<string> BloomLevels      { get; set; } = new();
    public string       Difficulty       { get; set; } = "mixed";
}
