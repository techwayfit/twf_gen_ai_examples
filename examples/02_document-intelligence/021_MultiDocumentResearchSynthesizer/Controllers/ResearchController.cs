using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TwfAiFramework.Nodes.AI;
using _021_MultiDocumentResearchSynthesizer.Services;

namespace _021_MultiDocumentResearchSynthesizer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ResearchController : ControllerBase
{
    private readonly ILogger<ResearchController>  _logger;
    private readonly IConfiguration               _configuration;
    private readonly QueryWorkflowService         _queryService;
    private readonly IngestWorkflowService        _ingestService;
    private readonly QdrantVectorStoreService     _vectorStore;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ResearchController(
        ILogger<ResearchController>  logger,
        IConfiguration               configuration,
        QueryWorkflowService         queryService,
        IngestWorkflowService        ingestService,
        QdrantVectorStoreService      vectorStore)
    {
        _logger        = logger;
        _configuration = configuration;
        _queryService  = queryService;
        _ingestService = ingestService;
        _vectorStore   = vectorStore;
    }

    // ── POST /api/Research/query ─────────────────────────────────────────────

    /// <summary>
    /// Accepts a research question and streams the synthesis pipeline back as SSE events.
    ///
    /// Event types emitted:
    ///   stage    — { message, stageIndex, totalStages }   pipeline progress
    ///   complete — SynthesisResult                        full structured answer
    ///   error    — { error }                              terminal error
    /// </summary>
    [HttpPost("query")]
    public async Task Query([FromBody] ResearchQueryRequest request, CancellationToken ct)
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

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            await SendAsync("error", new { error = Constants.Messages.EmptyQuestion });
            return;
        }

        if (request.Question.Length > 2_000)
        {
            await SendAsync("error", new { error = Constants.Messages.QuestionTooLong });
            return;
        }

        var (indexedChunks, _) = await _vectorStore.GetStatsAsync(request.Collection, ct);
        if (indexedChunks == 0)
        {
            await SendAsync("error", new { error = Constants.Messages.NoDocumentsIndexed });
            return;
        }

        var topK             = request.TopK is > 0 and <= 20 ? request.TopK : 8;
        var embeddingModel   = _configuration["OpenAI:EmbeddingModel"]   ?? "text-embedding-3-small";
        var embeddingEndpoint = _configuration["OpenAI:EmbeddingEndpoint"] ?? "https://api.openai.com/v1/embeddings";
        var llmConfig        = BuildLlmConfig(openAiKey);

        var query = new ResearchQuery
        {
            Question   = request.Question.Trim(),
            TopK       = topK,
            Collection = request.Collection?.Trim() ?? string.Empty,
        };

        try
        {
            var result = await _queryService.RunAsync(
                query:             query,
                sendStageAsync:    stage  => SendAsync("stage",    stage),
                sendCompleteAsync: result => SendAsync("complete", result),
                llmConfig:         llmConfig,
                apiKey:            openAiKey,
                embeddingModel:    embeddingModel,
                embeddingEndpoint: embeddingEndpoint,
                ct:                ct);

            if (!result.IsSuccess)
            {
                _logger.LogError("Query workflow failed: {Error}", result.ErrorMessage);
                await SendAsync("error", new { error = result.ErrorMessage ?? Constants.Messages.WorkflowFailed });
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during research synthesis");
            try { await SendAsync("error", new { error = Constants.Messages.UnexpectedError }); }
            catch { /* response may be gone */ }
        }
    }

    // ── POST /api/Research/ingest ────────────────────────────────────────────

    /// <summary>
    /// Accepts a document and indexes it into the Qdrant vector store.
    /// Returns a JSON summary with the number of chunks indexed.
    /// </summary>
    [HttpPost("ingest")]
    public async Task<IActionResult> Ingest([FromBody] IngestDocumentRequest request, CancellationToken ct)
    {
        var openAiKey = _configuration["OpenAI:ApiKey"];
        if (string.IsNullOrEmpty(openAiKey) || openAiKey == "your-openai-api-key-here")
            return BadRequest(new { error = Constants.Messages.OpenAiKeyNotConfigured });

        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = Constants.Messages.EmptyDocumentText });

        var maxDocChars = _configuration.GetValue<int>("Upload:MaxDocumentChars", 2_000_000);
        if (request.Text.Length > maxDocChars)
            return BadRequest(new { error = Constants.Messages.DocumentTooLong(maxDocChars) });

        var paperId = string.IsNullOrWhiteSpace(request.PaperId)
            ? $"doc_{Guid.NewGuid():N}"
            : request.PaperId.Trim();

        var embeddingModel    = _configuration["OpenAI:EmbeddingModel"]    ?? "text-embedding-3-small";
        var embeddingEndpoint = _configuration["OpenAI:EmbeddingEndpoint"] ?? "https://api.openai.com/v1/embeddings";

        var ingestRequest = new IngestRequest
        {
            Text         = request.Text.Trim(),
            PaperId      = paperId,
            Title        = request.Title?.Trim()   ?? paperId,
            Authors      = request.Authors?.Trim() ?? "Unknown",
            Year         = request.Year is > 1900 and <= 2100 ? request.Year : DateTime.UtcNow.Year,
            ChunkSize    = request.ChunkSize    is > 0  and <= 2000 ? request.ChunkSize    : 400,
            ChunkOverlap = request.ChunkOverlap is >= 0 and < 500   ? request.ChunkOverlap : 50,
            Collection   = request.Collection?.Trim() ?? string.Empty,
        };

        try
        {
            var summary = await _ingestService.RunAsync(
                ingestRequest, openAiKey, embeddingModel, embeddingEndpoint, ct);

            if (!summary.Success)
                return StatusCode(500, new { error = summary.Error ?? Constants.Messages.IngestFailed });

            var collection = ingestRequest.Collection;
            var (totalChunks, totalDocuments) = await _vectorStore.GetStatsAsync(collection, ct);
            return Ok(new
            {
                success        = true,
                paperId        = summary.PaperId,
                chunksIndexed  = summary.ChunksIndexed,
                collection,
                totalChunks,
                totalDocuments,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during document ingest");
            return StatusCode(500, new { error = Constants.Messages.UnexpectedError });
        }
    }

    // ── GET /api/Research/collections ────────────────────────────────────────

    [HttpGet("collections")]
    public async Task<IActionResult> Collections(CancellationToken ct)
    {
        var names = await _vectorStore.ListCollectionsAsync(ct);
        return Ok(names);
    }

    // ── GET /api/Research/status ─────────────────────────────────────────────

    [HttpGet("status")]
    public async Task<IActionResult> Status([FromQuery] string? collection, CancellationToken ct)
    {
        var (chunkCount, documentCount) = await _vectorStore.GetStatsAsync(collection, ct);
        return Ok(new { chunkCount, documentCount, collection });
    }

    // ── DELETE /api/Research/index ───────────────────────────────────────────

    [HttpDelete("index")]
    public async Task<IActionResult> ClearIndex([FromQuery] string? collection, CancellationToken ct)
    {
        await _vectorStore.ClearAsync(collection, ct);
        return Ok(new { message = "Index cleared.", collection });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private LlmConfig BuildLlmConfig(string apiKey)
    {
        var model    = _configuration["OpenAI:ChatModel"] ?? LlmConfig.OpenAI(apiKey).Model;
        var endpoint = _configuration["OpenAI:Endpoint"]  ?? LlmConfig.OpenAI(apiKey).ApiEndpoint;
        return LlmConfig.LmServer(model: model, apiKey: apiKey, apiEndpoint: endpoint);
    }
}

// ── Request models ────────────────────────────────────────────────────────────

public class ResearchQueryRequest
{
    public string  Question   { get; set; } = string.Empty;
    public int     TopK       { get; set; } = 8;
    public string? Collection { get; set; }
}

public class IngestDocumentRequest
{
    public string  Text         { get; set; } = string.Empty;
    public string? PaperId      { get; set; }
    public string? Title        { get; set; }
    public string? Authors      { get; set; }
    public int     Year         { get; set; } = DateTime.UtcNow.Year;
    public int     ChunkSize    { get; set; } = 400;
    public int     ChunkOverlap { get; set; } = 50;
    public string? Collection   { get; set; }
}
