using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using _025_ResumeParserCandidateRanker.Services;

namespace _025_ResumeParserCandidateRanker.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RankingController : ControllerBase
{
    private readonly ILogger<RankingController> _logger;
    private readonly IConfiguration             _configuration;
    private readonly RankingWorkflowService     _rankingService;
    private readonly ResumeRewriteService       _rewriteService;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public RankingController(
        ILogger<RankingController> logger,
        IConfiguration             configuration,
        RankingWorkflowService     rankingService,
        ResumeRewriteService       rewriteService)
    {
        _logger         = logger;
        _configuration  = configuration;
        _rankingService = rankingService;
        _rewriteService = rewriteService;
    }

    // ── POST /api/Ranking/rank ────────────────────────────────────────────────

    /// <summary>
    /// Accepts a job description and a list of extracted resume texts,
    /// then streams the ranking pipeline back as SSE events.
    ///
    /// Event types emitted:
    ///   stage    — { message, stageIndex, totalStages }    pipeline progress
    ///   complete — RankingResult                            full structured result
    ///   error    — { error }                               terminal error
    /// </summary>
    [HttpPost("rank")]
    public async Task Rank([FromBody] RankingApiRequest request, CancellationToken ct)
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

        if (string.IsNullOrWhiteSpace(request.JobDescription))
        {
            await SendAsync("error", new { error = Constants.Messages.EmptyJobDescription });
            return;
        }

        if (request.Resumes is null || request.Resumes.Count == 0)
        {
            await SendAsync("error", new { error = Constants.Messages.NoResumesProvided });
            return;
        }

        var embeddingModel    = _configuration["OpenAI:EmbeddingModel"]    ?? "text-embedding-3-small";
        var embeddingEndpoint = _configuration["OpenAI:EmbeddingEndpoint"] ?? "https://api.openai.com/v1/embeddings";
        var llmModel          = _configuration["OpenAI:ChatModel"]         ?? "gpt-4o-mini";
        var llmEndpoint       = _configuration["OpenAI:Endpoint"]          ?? "https://api.openai.com/v1/chat/completions";

        var rankingInput = new RankingInput
        {
            JobDescription      = request.JobDescription.Trim(),
            Resumes             = request.Resumes
                .Select(r => new ResumeInput(r.FileName, r.Text))
                .ToList(),
            TopN               = request.TopN is > 0 and <= 20 ? request.TopN : 5,
            SimilarityThreshold = request.SimilarityThreshold is >= 0f and <= 1f
                ? request.SimilarityThreshold
                : 0f,
        };

        try
        {
            var result = await _rankingService.RunAsync(
                request:           rankingInput,
                sendStageAsync:    stage  => SendAsync("stage",    stage),
                sendCompleteAsync: result => SendAsync("complete", result),
                apiKey:            openAiKey,
                embeddingModel:    embeddingModel,
                embeddingEndpoint: embeddingEndpoint,
                llmModel:          llmModel,
                llmEndpoint:       llmEndpoint,
                ct:                ct);

            if (!result.IsSuccess)
            {
                _logger.LogError("Ranking workflow failed: {Error}", result.ErrorMessage);
                await SendAsync("error",
                    new { error = result.ErrorMessage ?? Constants.Messages.WorkflowFailed });
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during candidate ranking");
            try { await SendAsync("error", new { error = Constants.Messages.UnexpectedError }); }
            catch { /* response may be gone */ }
        }
    }

    // ── POST /api/Ranking/rewrite ─────────────────────────────────────────────

    /// <summary>
    /// Accepts a job description and a single resume text, rewrites the resume
    /// to maximise relevance, and streams the result as SSE.
    ///
    /// Event types emitted:
    ///   stage    — { message, stageIndex, totalStages }  pipeline progress
    ///   complete — { html }                              self-contained HTML resume
    ///   error    — { error }                             terminal error
    /// </summary>
    [HttpPost("rewrite")]
    public async Task Rewrite([FromBody] RewriteApiRequest request, CancellationToken ct)
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

        if (string.IsNullOrWhiteSpace(request.JobDescription))
        {
            await SendAsync("error", new { error = Constants.Messages.EmptyJobDescription });
            return;
        }

        if (string.IsNullOrWhiteSpace(request.ResumeText))
        {
            await SendAsync("error", new { error = "Resume text cannot be empty." });
            return;
        }

        var llmModel    = _configuration["OpenAI:ChatModel"]  ?? "gpt-4o-mini";
        var llmEndpoint = _configuration["OpenAI:Endpoint"]   ?? "https://api.openai.com/v1/chat/completions";

        try
        {
            await SendAsync("stage", new { message = "Tailoring resume to job requirements...", stageIndex = 1, totalStages = 1 });

            var html = await _rewriteService.RewriteAsync(
                jobDescription: request.JobDescription.Trim(),
                resumeText:     request.ResumeText,
                apiKey:         openAiKey,
                model:          llmModel,
                endpoint:       llmEndpoint,
                ct:             ct);

            await SendAsync("complete", new { html });
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during resume rewrite");
            try { await SendAsync("error", new { error = Constants.Messages.UnexpectedError }); }
            catch { /* response may be gone */ }
        }
    }
}

// ── Request models ────────────────────────────────────────────────────────────

public class RankingApiRequest
{
    public string                 JobDescription      { get; set; } = string.Empty;
    public List<ResumeTextInput>  Resumes             { get; set; } = new();
    public int                    TopN               { get; set; } = 5;
    public float                  SimilarityThreshold { get; set; } = 0f;
}

public class ResumeTextInput
{
    public string FileName { get; set; } = string.Empty;
    public string Text     { get; set; } = string.Empty;
}

public class RewriteApiRequest
{
    public string JobDescription { get; set; } = string.Empty;
    public string ResumeText     { get; set; } = string.Empty;
    public string CandidateName  { get; set; } = string.Empty;
}
