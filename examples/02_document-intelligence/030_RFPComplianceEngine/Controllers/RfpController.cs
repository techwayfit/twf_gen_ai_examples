using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using _030_RFPComplianceEngine.Services;

namespace _030_RFPComplianceEngine.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RfpController : ControllerBase
{
    private readonly ILogger<RfpController>               _logger;
    private readonly IConfiguration                       _configuration;
    private readonly RfpComplianceWorkflowService         _workflowService;
    private readonly ContractQueryWorkflowService         _queryService;
    private readonly IngestWorkflowService                _ingestService;
    private readonly QdrantVectorStoreService             _vectorStore;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public RfpController(
        ILogger<RfpController>               logger,
        IConfiguration                       configuration,
        RfpComplianceWorkflowService         workflowService,
        ContractQueryWorkflowService         queryService,
        IngestWorkflowService                ingestService,
        QdrantVectorStoreService             vectorStore)
    {
        _logger          = logger;
        _configuration   = configuration;
        _workflowService = workflowService;
        _queryService    = queryService;
        _ingestService   = ingestService;
        _vectorStore     = vectorStore;
    }

    // ── POST /api/Rfp/analyze ────────────────────────────────────────────────

    [HttpPost("analyze")]
    public async Task Analyze([FromBody] RfpAnalyzeApiRequest request, CancellationToken ct)
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

        if (string.IsNullOrWhiteSpace(request.RfpText))
        {
            await SendAsync("error", new { error = Constants.Messages.EmptyRfpText });
            return;
        }

        if (string.IsNullOrWhiteSpace(request.CapabilitiesText))
        {
            await SendAsync("error", new { error = Constants.Messages.NoCapabilities });
            return;
        }

        var llmModel         = _configuration["OpenAI:ChatModel"]         ?? "gpt-4o-mini";
        var llmEndpoint      = _configuration["OpenAI:Endpoint"]          ?? "https://api.openai.com/v1/chat/completions";
        var embeddingModel   = _configuration["OpenAI:EmbeddingModel"]    ?? "text-embedding-3-small";
        var embeddingEndpoint = _configuration["OpenAI:EmbeddingEndpoint"] ?? "https://api.openai.com/v1/embeddings";

        var analysisRequest = new RfpAnalysisRequest
        {
            RfpText                = request.RfpText.Trim(),
            CapabilitiesText       = request.CapabilitiesText.Trim(),
            PoliciesText           = request.PoliciesText?.Trim()  ?? string.Empty,
            RegulationsText        = request.RegulationsText?.Trim() ?? string.Empty,
            Frameworks             = request.Frameworks?.Count > 0 ? request.Frameworks : new() { "GDPR", "SOC2" },
            CapabilitiesCollection = request.CapabilitiesCollection?.Trim() ?? (_configuration["Qdrant:Collections:Capabilities"] ?? "capabilities"),
            PoliciesCollection     = request.PoliciesCollection?.Trim()     ?? (_configuration["Qdrant:Collections:Policies"] ?? "policies"),
            RegulationsCollection  = request.RegulationsCollection?.Trim()  ?? (_configuration["Qdrant:Collections:Regulations"] ?? "regulations"),
            ChunkSize              = 400,
            ChunkOverlap           = 50,
        };

        try
        {
            var result = await _workflowService.RunAsync(
                request:           analysisRequest,
                sendStageAsync:    stage  => SendAsync("stage",    stage),
                sendCompleteAsync: result => SendAsync("complete", result),
                apiKey:            openAiKey,
                llmModel:          llmModel,
                llmEndpoint:       llmEndpoint,
                embeddingModel:    embeddingModel,
                embeddingEndpoint: embeddingEndpoint,
                ct:                ct);

            if (!result.IsSuccess)
            {
                _logger.LogError("RFP analysis workflow failed: {Error}", result.ErrorMessage);
                await SendAsync("error",
                    new { error = result.ErrorMessage ?? Constants.Messages.WorkflowFailed });
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during RFP analysis");
            try { await SendAsync("error", new { error = Constants.Messages.UnexpectedError }); }
            catch { /* response may be gone */ }
        }
    }

    // ── POST /api/Rfp/query ───────────────────────────────────────────────────

    [HttpPost("query")]
    public async Task Query([FromBody] ContractQueryApiRequest request, CancellationToken ct)
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
            await SendAsync("error", new { error = "Question cannot be empty." });
            return;
        }

        var collection = !string.IsNullOrWhiteSpace(request.Collection) ? request.Collection.Trim()
                         : (_configuration["Qdrant:Collections:Contracts"] ?? "contracts");

        _logger.LogInformation("Query: collection={Collection}", collection);

        var (indexedChunks, _) = await _vectorStore.GetStatsAsync(collection, ct);
        if (indexedChunks == 0)
        {
            var capsCount  = (await _vectorStore.GetStatsAsync(_configuration["Qdrant:Collections:Capabilities"] ?? "capabilities", ct)).ChunkCount;
            var polCount   = (await _vectorStore.GetStatsAsync(_configuration["Qdrant:Collections:Policies"] ?? "policies", ct)).ChunkCount;
            var regsCount  = (await _vectorStore.GetStatsAsync(_configuration["Qdrant:Collections:Regulations"] ?? "regulations", ct)).ChunkCount;
            var contrCount = (await _vectorStore.GetStatsAsync(_configuration["Qdrant:Collections:Contracts"] ?? "contracts", ct)).ChunkCount;

            await SendAsync("error", new
            {
                error = $"No contracts indexed in collection '{collection}'. " +
                        $"Index status: capabilities={capsCount}, policies={polCount}, regulations={regsCount}, contracts={contrCount}. " +
                        $"Make sure you ingest documents with docType='contract'."
            });
            return;
        }

        var topK             = request.TopK is > 0 and <= 20 ? request.TopK : 8;
        var llmModel         = _configuration["OpenAI:ChatModel"]          ?? "gpt-4o-mini";
        var llmEndpoint      = _configuration["OpenAI:Endpoint"]           ?? "https://api.openai.com/v1/chat/completions";
        var embeddingModel   = _configuration["OpenAI:EmbeddingModel"]     ?? "text-embedding-3-small";
        var embeddingEndpoint = _configuration["OpenAI:EmbeddingEndpoint"] ?? "https://api.openai.com/v1/embeddings";

        var query = new ContractQuery
        {
            Question   = request.Question.Trim(),
            TopK       = topK,
            Collection = collection,
        };

        try
        {
            var result = await _queryService.RunAsync(
                query:             query,
                sendStageAsync:    stage  => SendAsync("stage",    stage),
                sendCompleteAsync: result => SendAsync("complete", result),
                apiKey:            openAiKey,
                llmModel:          llmModel,
                llmEndpoint:       llmEndpoint,
                embeddingModel:    embeddingModel,
                embeddingEndpoint: embeddingEndpoint,
                ct:                ct);

            if (!result.IsSuccess)
            {
                _logger.LogError("Contract query workflow failed: {Error}", result.ErrorMessage);
                await SendAsync("error", new { error = result.ErrorMessage ?? Constants.Messages.WorkflowFailed });
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during contract query");
            try { await SendAsync("error", new { error = Constants.Messages.UnexpectedError }); }
            catch { /* response may be gone */ }
        }
    }

    // ── POST /api/Rfp/ingest ─────────────────────────────────────────────────

    [HttpPost("ingest")]
    public async Task<IActionResult> Ingest([FromBody] RfpIngestApiRequest request, CancellationToken ct)
    {
        var openAiKey = _configuration["OpenAI:ApiKey"];
        if (string.IsNullOrEmpty(openAiKey) || openAiKey == "your-openai-api-key-here")
            return BadRequest(new { error = Constants.Messages.OpenAiKeyNotConfigured });

        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "Document text cannot be empty." });

        var maxDocChars = _configuration.GetValue<int>("Upload:MaxDocumentChars", 2_000_000);
        if (request.Text.Length > maxDocChars)
            return BadRequest(new { error = Constants.Messages.DocumentTooLong(maxDocChars) });

        var docType = request.DocType?.ToLower() switch
        {
            "capability" or "capabilities" => "capability",
            "policy" or "policies"       => "policy",
            "regulation" or "regulations" => "regulation",
            "contract" or "contracts"    => "contract",
            _                             => "capability"
        };

        var collection = docType switch
        {
            "capability" => !string.IsNullOrWhiteSpace(request.Collection) ? request.Collection.Trim() : (_configuration["Qdrant:Collections:Capabilities"] ?? "capabilities"),
            "policy"     => !string.IsNullOrWhiteSpace(request.Collection) ? request.Collection.Trim() : (_configuration["Qdrant:Collections:Policies"] ?? "policies"),
            "regulation" => !string.IsNullOrWhiteSpace(request.Collection) ? request.Collection.Trim() : (_configuration["Qdrant:Collections:Regulations"] ?? "regulations"),
            "contract"   => !string.IsNullOrWhiteSpace(request.Collection) ? request.Collection.Trim() : (_configuration["Qdrant:Collections:Contracts"] ?? "contracts"),
            _            => !string.IsNullOrWhiteSpace(request.Collection) ? request.Collection.Trim() : (_configuration["Qdrant:Collections:Capabilities"] ?? "capabilities"),
        };

        _logger.LogInformation("Ingest: docType={DocType} -> collection={Collection}", docType, collection);

        var documentId = string.IsNullOrWhiteSpace(request.DocumentId)
            ? $"doc_{docType}_{Guid.NewGuid():N}"
            : request.DocumentId.Trim();

        var embeddingModel    = _configuration["OpenAI:EmbeddingModel"]    ?? "text-embedding-3-small";
        var embeddingEndpoint = _configuration["OpenAI:EmbeddingEndpoint"] ?? "https://api.openai.com/v1/embeddings";

        var ingestRequest = new IngestRequest
        {
            Text         = request.Text.Trim(),
            DocumentId   = documentId,
            Title        = request.Title?.Trim() ?? documentId,
            DocType      = docType,
            ChunkSize    = request.ChunkSize    is > 0  and <= 2000 ? request.ChunkSize    : 400,
            ChunkOverlap = request.ChunkOverlap is >= 0 and < 500   ? request.ChunkOverlap : 50,
            Collection   = collection,
        };

        try
        {
            var summary = await _ingestService.RunAsync(
                ingestRequest, openAiKey, embeddingModel, embeddingEndpoint, ct);

            if (!summary.Success)
                return StatusCode(500, new { error = summary.Error ?? Constants.Messages.IngestFailed });

            var (totalChunks, totalDocuments) = await _vectorStore.GetStatsAsync(collection, ct);
            return Ok(new
            {
                success        = true,
                documentId     = summary.DocumentId,
                chunksIndexed  = summary.ChunksIndexed,
                docType,
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

    // ── GET /api/Rfp/status ──────────────────────────────────────────────────

    [HttpGet("status")]
    public async Task<IActionResult> Status([FromQuery] string? collection, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(collection))
        {
            var capsColl  = _configuration["Qdrant:Collections:Capabilities"] ?? "capabilities";
            var polColl   = _configuration["Qdrant:Collections:Policies"]     ?? "policies";
            var regsColl  = _configuration["Qdrant:Collections:Regulations"]  ?? "regulations";
            var contrColl = _configuration["Qdrant:Collections:Contracts"]    ?? "contracts";

            var capsStats   = await _vectorStore.GetStatsAsync(capsColl, ct);
            var polStats    = await _vectorStore.GetStatsAsync(polColl, ct);
            var regsStats   = await _vectorStore.GetStatsAsync(regsColl, ct);
            var contrStats  = await _vectorStore.GetStatsAsync(contrColl, ct);
            return Ok(new
            {
                capabilities = new { capsStats.ChunkCount, capsStats.DocumentCount },
                policies     = new { polStats.ChunkCount,  polStats.DocumentCount  },
                regulations  = new { regsStats.ChunkCount, regsStats.DocumentCount },
                contracts    = new { contrStats.ChunkCount, contrStats.DocumentCount },
            });
        }

        var stats = await _vectorStore.GetStatsAsync(collection, ct);
        return Ok(new { collection, stats.ChunkCount, stats.DocumentCount });
    }

    // ── DELETE /api/Rfp/index ────────────────────────────────────────────────

    [HttpDelete("index")]
    public async Task<IActionResult> ClearIndex([FromQuery] string collection, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(collection))
            return BadRequest(new { error = "Collection name is required." });

        await _vectorStore.ClearAsync(collection, ct);
        return Ok(new { message = "Index cleared.", collection });
    }
}

// ── Request models ────────────────────────────────────────────────────────────

public class RfpAnalyzeApiRequest
{
    public string       RfpText                 { get; set; } = string.Empty;
    public string       CapabilitiesText        { get; set; } = string.Empty;
    public string?      PoliciesText            { get; set; }
    public string?      RegulationsText         { get; set; }
    public List<string>? Frameworks             { get; set; }
    public string?      CapabilitiesCollection  { get; set; }
    public string?      PoliciesCollection      { get; set; }
    public string?      RegulationsCollection   { get; set; }
}

public class RfpIngestApiRequest
{
    public string  Text         { get; set; } = string.Empty;
    public string? DocumentId   { get; set; }
    public string? Title        { get; set; }
    public string? DocType      { get; set; }
    public int     ChunkSize    { get; set; } = 400;
    public int     ChunkOverlap { get; set; } = 50;
    public string? Collection   { get; set; }
}

public class ContractQueryApiRequest
{
    public string  Question   { get; set; } = string.Empty;
    public int     TopK       { get; set; } = 8;
    public string? Collection { get; set; }
}
