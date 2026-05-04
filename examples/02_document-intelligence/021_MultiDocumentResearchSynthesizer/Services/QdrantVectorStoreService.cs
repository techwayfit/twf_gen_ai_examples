using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace _021_MultiDocumentResearchSynthesizer.Services;

/// <summary>
/// A single indexed chunk from a research paper.
/// </summary>
public record VectorChunk(
    string   Id,
    string   Text,
    float[]  Embedding,
    string   PaperId,
    string   Title,
    string   Authors,
    int      Year,
    int      Page,
    int      ChunkIndex);

/// <summary>
/// Qdrant-backed vector store service.
/// Each logical "collection" is a separate Qdrant collection, allowing documents to be
/// grouped and queried independently. All methods accept an optional <paramref name="collectionName"/>
/// override; if omitted the value from <c>Qdrant:CollectionName</c> config is used.
/// </summary>
public class QdrantVectorStoreService(
    QdrantClient                      client,
    ILogger<QdrantVectorStoreService> logger,
    IConfiguration                    config)
{
    private string DefaultCollection => config["Qdrant:CollectionName"] ?? "research_papers";
    private int    VectorSize        => int.TryParse(config["Qdrant:VectorSize"], out var s) ? s : 1536;

    private string Resolve(string? name) =>
        string.IsNullOrWhiteSpace(name) ? DefaultCollection : name.Trim();

    // ── Collection lifecycle ──────────────────────────────────────────────────

    /// <summary>Creates the Qdrant collection if it does not already exist.</summary>
    public async Task EnsureCollectionExistsAsync(string? collectionName = null, CancellationToken ct = default)
    {
        var name = Resolve(collectionName);
        var collections = await client.ListCollectionsAsync(ct);
        if (collections.Contains(name)) return;

        await client.CreateCollectionAsync(
            collectionName: name,
            vectorsConfig:  new VectorParams { Size = (ulong)VectorSize, Distance = Distance.Cosine },
            cancellationToken: ct);

        logger.LogInformation("Created Qdrant collection '{Name}'", name);
    }

    /// <summary>Returns all existing Qdrant collection names.</summary>
    public async Task<List<string>> ListCollectionsAsync(CancellationToken ct = default)
    {
        var names = await client.ListCollectionsAsync(ct);
        return names.OrderBy(n => n).ToList();
    }

    // ── Upsert ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Upserts all chunks for a paper into the specified collection.
    /// Creates the collection first if it doesn't exist.
    /// Deletes any existing chunks for the same paper_id (idempotent re-ingest).
    /// </summary>
    public async Task UpsertChunksAsync(IEnumerable<VectorChunk> chunks, string? collectionName = null, CancellationToken ct = default)
    {
        var name = Resolve(collectionName);
        var list = chunks.ToList();
        if (list.Count == 0) return;

        await EnsureCollectionExistsAsync(name, ct);

        var paperId = list[0].PaperId;
        await DeleteByPaperIdAsync(paperId, name, ct);

        var points = list.Select(c =>
        {
            var v = new Vector();
            v.Data.AddRange(c.Embedding);

            var point = new PointStruct
            {
                Id      = new PointId { Uuid = Guid.NewGuid().ToString() },
                Vectors = new Qdrant.Client.Grpc.Vectors { Vector = v },
            };

            point.Payload["text"]        = c.Text;
            point.Payload["paper_id"]    = c.PaperId;
            point.Payload["title"]       = c.Title;
            point.Payload["authors"]     = c.Authors;
            point.Payload["year"]        = (long)c.Year;
            point.Payload["page"]        = (long)c.Page;
            point.Payload["chunk_index"] = (long)c.ChunkIndex;
            point.Payload["chunk_id"]    = c.Id;

            return point;
        }).ToList();

        await client.UpsertAsync(name, points, cancellationToken: ct);
        logger.LogInformation("Upserted {Count} chunks for paper '{PaperId}' in '{Collection}'", list.Count, paperId, name);
    }

    // ── Search ────────────────────────────────────────────────────────────────

    /// <summary>Returns the top-k most semantically similar chunks for a query vector.</summary>
    public async Task<List<VectorChunk>> SearchAsync(
        float[]           queryVector,
        int               topK           = 8,
        string?           collectionName = null,
        CancellationToken ct             = default)
    {
        var name = Resolve(collectionName);
        try
        {
            var results = await client.SearchAsync(
                collectionName:  name,
                vector:          queryVector,
                limit:           (ulong)topK,
                payloadSelector: new WithPayloadSelector { Enable = true },
                cancellationToken: ct);

            return results.Select(r => new VectorChunk(
                Id:         GetStr(r.Payload, "chunk_id"),
                Text:       GetStr(r.Payload, "text"),
                Embedding:  Array.Empty<float>(),
                PaperId:    GetStr(r.Payload, "paper_id"),
                Title:      GetStr(r.Payload, "title"),
                Authors:    GetStr(r.Payload, "authors"),
                Year:       GetInt(r.Payload, "year"),
                Page:       GetInt(r.Payload, "page"),
                ChunkIndex: GetInt(r.Payload, "chunk_index")
            )).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Qdrant search failed in '{Collection}' — returning empty results", name);
            return new List<VectorChunk>();
        }
    }

    // ── Stats ─────────────────────────────────────────────────────────────────

    public async Task<(int ChunkCount, int DocumentCount)> GetStatsAsync(string? collectionName = null, CancellationToken ct = default)
    {
        var name = Resolve(collectionName);
        try
        {
            var chunkCount = await client.CountAsync(name, cancellationToken: ct);
            // Approximate document count from distinct paper_id values via scroll
            var scrollResult = await client.ScrollAsync(name,
                limit: 1000,
                payloadSelector: new WithPayloadSelector { Enable = true },
                cancellationToken: ct);
            var docCount = scrollResult.Result.Select(p => GetStr(p.Payload, "paper_id"))
                                              .Distinct()
                                              .Count();
            return ((int)chunkCount, docCount);
        }
        catch
        {
            return (0, 0);
        }
    }

    // ── Clear ─────────────────────────────────────────────────────────────────

    public async Task ClearAsync(string? collectionName = null, CancellationToken ct = default)
    {
        var name = Resolve(collectionName);
        await client.DeleteCollectionAsync(name, cancellationToken: ct);
        await EnsureCollectionExistsAsync(name, ct);
        logger.LogInformation("Cleared Qdrant collection '{Name}'", name);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task DeleteByPaperIdAsync(string paperId, string collectionName, CancellationToken ct)
    {
        var filter = new Filter();
        filter.Must.Add(new Condition
        {
            Field = new FieldCondition
            {
                Key   = "paper_id",
                Match = new Match { Keyword = paperId },
            }
        });
        await client.DeleteAsync(collectionName, filter: filter, cancellationToken: ct);
    }

    private static string GetStr(IDictionary<string, Value> payload, string key) =>
        payload.TryGetValue(key, out var v) ? v.StringValue : string.Empty;

    private static int GetInt(IDictionary<string, Value> payload, string key) =>
        payload.TryGetValue(key, out var v) ? (int)v.IntegerValue : 0;
}
