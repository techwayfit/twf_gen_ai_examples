using Twf.Flow.Core;
using Twf.Flow.Core.Extensions;
using Twf.Flow.Nodes.Control;
using Twf.Flow.Nodes.Data;

namespace _030_RFPComplianceEngine.Services;

public class IngestWorkflowService(
    ILogger<IngestWorkflowService> logger,
    IConfiguration                 configuration,
    QdrantVectorStoreService       vectorStore,
    ChunkingService                chunkingService,
    EmbeddingService               embeddingService)
{
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
            .Set("document_id",   request.DocumentId)
            .Set("title",         request.Title)
            .Set("doc_type",      request.DocType)
            .Set("chunk_size",    request.ChunkSize.ToString())
            .Set("chunk_overlap", request.ChunkOverlap.ToString())
            .Set("collection",    request.Collection);

        var context = new WorkflowContext("DocumentIngest", logger);
        var result  = await workflow.RunAsync(input, context, ct);

        if (!result.IsSuccess)
        {
            logger.LogError("Ingest workflow failed: {Error}", result.ErrorMessage);
            return new IngestSummary(Success: false, ChunksIndexed: 0, DocumentId: request.DocumentId, Error: result.ErrorMessage);
        }

        var indexed = int.TryParse(result.Data?.GetString("chunks_indexed"), out var n) ? n : 0;
        return new IngestSummary(Success: true, ChunksIndexed: indexed, DocumentId: request.DocumentId, Error: null);
    }

    private Workflow BuildWorkflow(
        IngestRequest     request,
        string            apiKey,
        string            embeddingModel,
        string            embeddingEndpoint,
        CancellationToken ct = default)
    {
        var workflow = Workflow.Create("DocumentIngest").UseLogger(logger);

        workflow.AddNode(
            new FilterNode("ValidateInput")
                .RequireNonEmpty("document_text")
                .RequireNonEmpty("document_id")
                .MaxLength("document_text", configuration.GetValue<int>("Upload:MaxDocumentChars", 2_000_000)));

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

        workflow.AddStep("EmbedChunks", async (data, _) =>
        {
            var rawChunks = data.Get<List<(int Index, string Text)>>("raw_chunks") ?? new();
            if (rawChunks.Count == 0) return data;

            const int batchSize    = 200;
            const int maxChunkChars = 8_000;
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

        workflow.AddStep("StoreVectors", async (data, _) =>
        {
            var rawChunks = data.Get<List<(int Index, string Text)>>("raw_chunks") ?? new();
            var vectors   = data.Get<List<float[]>>("chunk_vectors") ?? new();

            var documentId = data.GetString("document_id") ?? "unknown";
            var title      = data.GetString("title")       ?? string.Empty;
            var docType    = data.GetString("doc_type")    ?? "unknown";

            var vectorChunks = rawChunks
                .Zip(vectors, (chunk, vec) => new VectorChunk(
                    Id:         $"{documentId}_chunk_{chunk.Index}",
                    Text:       chunk.Text,
                    Embedding:  vec,
                    DocumentId: documentId,
                    Title:      title,
                    DocType:    docType,
                    ChunkIndex: chunk.Index))
                .ToList();

            var collection = data.GetString("collection") ?? "capabilities";
            await vectorStore.UpsertChunksAsync(vectorChunks, collection, ct);

            data.Set("chunks_indexed", vectorChunks.Count.ToString());
            logger.LogInformation(
                "Stored {Count} chunks for document '{DocId}' in '{Collection}'",
                vectorChunks.Count, documentId, collection);

            return data;
        });

        return workflow;
    }
}

public class IngestRequest
{
    public string Text         { get; set; } = string.Empty;
    public string DocumentId   { get; set; } = string.Empty;
    public string Title        { get; set; } = string.Empty;
    public string DocType      { get; set; } = "capability";
    public int    ChunkSize    { get; set; } = 400;
    public int    ChunkOverlap { get; set; } = 50;
    public string Collection   { get; set; } = "capabilities";
}

public record IngestSummary(
    bool   Success,
    int    ChunksIndexed,
    string DocumentId,
    string? Error);
