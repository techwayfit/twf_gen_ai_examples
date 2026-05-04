using Microsoft.Extensions.Configuration;
using TwfAiFramework.Core;
using TwfAiFramework.Core.Extensions;
using TwfAiFramework.Nodes.Control;
using TwfAiFramework.Nodes.Data;

namespace _021_MultiDocumentResearchSynthesizer.Services;

/// <summary>
/// Builds and executes the document ingest pipeline.
///
/// Pipeline stages:
///   1. ValidateInput  — FilterNode: ensure document text and metadata are present
///   2. ChunkDocument  — AddStep:    split text into overlapping word-based chunks
///   3. EmbedChunks    — AddStep:    batch-embed all chunks via OpenAI API
///   4. StoreVectors   — AddStep:    upsert chunk vectors into QdrantVectorStoreService
/// </summary>
public class IngestWorkflowService(
    ILogger<IngestWorkflowService> logger,
    IConfiguration                 configuration,
    QdrantVectorStoreService       vectorStore,
    ChunkingService                chunkingService,
    EmbeddingService               embeddingService)
{
    // ── Public entry point ────────────────────────────────────────────────────

    public async Task<IngestSummary> RunAsync(
        IngestRequest     request,
        string            apiKey,
        string            embeddingModel,
        string            embeddingEndpoint,
        CancellationToken ct = default)
    {
        var workflow = BuildWorkflow(request, apiKey, embeddingModel, embeddingEndpoint, ct);

        var input = WorkflowData
            .From("document_text", request.Text)
            .Set("paper_id",      request.PaperId)
            .Set("title",         request.Title)
            .Set("authors",       request.Authors)
            .Set("year",          request.Year.ToString())
            .Set("chunk_size",    request.ChunkSize.ToString())
            .Set("chunk_overlap", request.ChunkOverlap.ToString())
            .Set("collection",    request.Collection);

        var context = new WorkflowContext("PaperIngest", logger);
        var result  = await workflow.RunAsync(input, context, ct);

        if (!result.IsSuccess)
        {
            logger.LogError("Ingest workflow failed: {Error}", result.ErrorMessage);
            return new IngestSummary(Success: false, ChunksIndexed: 0, PaperId: request.PaperId, Error: result.ErrorMessage);
        }

        var indexed = int.TryParse(result.Data?.GetString("chunks_indexed"), out var n) ? n : 0;
        return new IngestSummary(Success: true, ChunksIndexed: indexed, PaperId: request.PaperId, Error: null);
    }

    // ── Workflow builder ──────────────────────────────────────────────────────

    private Workflow BuildWorkflow(
        IngestRequest     request,
        string            apiKey,
        string            embeddingModel,
        string            embeddingEndpoint,
        CancellationToken ct = default)
    {
        var workflow = Workflow.Create("PaperIngest").UseLogger(logger);

        // ── 1. Validate input ────────────────────────────────────────────────
        workflow.AddNode(
            new FilterNode("ValidateInput")
                .RequireNonEmpty("document_text")
                .RequireNonEmpty("paper_id")
                .MaxLength("document_text", configuration.GetValue<int>("Upload:MaxDocumentChars", 2_000_000)));

        // ── 2. Chunk document ────────────────────────────────────────────────
        workflow.AddStep("ChunkDocument", async (data, _) =>
        {
            var text         = data.GetString("document_text") ?? string.Empty;
            int chunkSize    = int.TryParse(data.GetString("chunk_size"),    out var cs) ? cs : 400;
            int chunkOverlap = int.TryParse(data.GetString("chunk_overlap"), out var co) ? co : 50;

            var rawChunks = chunkingService.Chunk(text, chunkSize, chunkOverlap);
            data.Set("raw_chunks", rawChunks);

            logger.LogInformation("Document chunked into {Count} chunks", rawChunks.Count);
            return data;
        });

        // ── 3. Batch-embed all chunks ────────────────────────────────────────
        workflow.AddStep("EmbedChunks", async (data, _) =>
        {
            var rawChunks = data.Get<List<(int Index, string Text)>>("raw_chunks") ?? new();
            if (rawChunks.Count == 0) return data;

            const int batchSize   = 200;
            const int maxChunkChars = 8_000; // worst-case 1 char/token keeps us under the 8 192-token limit
            var texts = rawChunks
                .Select(c => c.Text.Length > maxChunkChars ? c.Text[..maxChunkChars] : c.Text)
                .ToList();
            var vectors = new List<float[]>(texts.Count);

            for (int i = 0; i < texts.Count; i += batchSize)
            {
                var batch = texts.Skip(i).Take(batchSize);
                var batchVectors = await embeddingService.EmbedBatchAsync(
                    batch, apiKey, embeddingModel, embeddingEndpoint, ct);
                vectors.AddRange(batchVectors);
            }

            data.Set("chunk_vectors", vectors);
            return data;
        });

        // ── 4. Store vectors in the Qdrant index ──────────────────────────────
        workflow.AddStep("StoreVectors", async (data, _) =>
        {
            var rawChunks = data.Get<List<(int Index, string Text)>>("raw_chunks") ?? new();
            var vectors   = data.Get<List<float[]>>("chunk_vectors") ?? new();

            var paperId = data.GetString("paper_id") ?? "unknown";
            var title   = data.GetString("title")    ?? string.Empty;
            var authors = data.GetString("authors")  ?? string.Empty;
            int year    = int.TryParse(data.GetString("year"), out var y) ? y : 0;

            var vectorChunks = rawChunks
                .Zip(vectors, (chunk, vec) => new VectorChunk(
                    Id:         $"{paperId}_chunk_{chunk.Index}",
                    Text:       chunk.Text,
                    Embedding:  vec,
                    PaperId:    paperId,
                    Title:      title,
                    Authors:    authors,
                    Year:       year,
                    Page:       0,
                    ChunkIndex: chunk.Index))
                .ToList();

            var collection = data.GetString("collection") ?? string.Empty;
            await vectorStore.UpsertChunksAsync(vectorChunks, collection, ct);

            data.Set("chunks_indexed", vectorChunks.Count.ToString());
            logger.LogInformation(
                "Stored {Count} chunks for paper '{PaperId}'",
                vectorChunks.Count, paperId);

            return data;
        });

        return workflow;
    }
}

// ── Request / result types ────────────────────────────────────────────────────

public class IngestRequest
{
    public string Text         { get; set; } = string.Empty;
    public string PaperId      { get; set; } = string.Empty;
    public string Title        { get; set; } = string.Empty;
    public string Authors      { get; set; } = string.Empty;
    public int    Year         { get; set; } = DateTime.UtcNow.Year;
    public int    ChunkSize    { get; set; } = 400;
    public int    ChunkOverlap { get; set; } = 50;
    public string Collection   { get; set; } = string.Empty;
}

public record IngestSummary(
    bool   Success,
    int    ChunksIndexed,
    string PaperId,
    string? Error);
