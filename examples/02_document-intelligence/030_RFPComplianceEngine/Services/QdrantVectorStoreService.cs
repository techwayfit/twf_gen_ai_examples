using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace _030_RFPComplianceEngine.Services;

public record VectorChunk(
    string   Id,
    string   Text,
    float[]  Embedding,
    string   DocumentId,
    string   Title,
    string   DocType,
    int      ChunkIndex);

public class QdrantVectorStoreService(
    QdrantClient                      client,
    ILogger<QdrantVectorStoreService> logger,
    IConfiguration                    config)
{
    private int VectorSize => int.TryParse(config["Qdrant:VectorSize"], out var s) ? s : 1536;

    public async Task EnsureCollectionExistsAsync(string collectionName, CancellationToken ct = default)
    {
        var collections = await client.ListCollectionsAsync(ct);
        if (collections.Contains(collectionName)) return;

        await client.CreateCollectionAsync(
            collectionName:  collectionName,
            vectorsConfig:   new VectorParams { Size = (ulong)VectorSize, Distance = Distance.Cosine },
            cancellationToken: ct);

        logger.LogInformation("Created Qdrant collection '{Name}'", collectionName);
    }

    public async Task<List<string>> ListCollectionsAsync(CancellationToken ct = default)
    {
        var names = await client.ListCollectionsAsync(ct);
        return names.OrderBy(n => n).ToList();
    }

    public async Task UpsertChunksAsync(IEnumerable<VectorChunk> chunks, string collectionName, CancellationToken ct = default)
    {
        var list = chunks.ToList();
        if (list.Count == 0) return;

        await EnsureCollectionExistsAsync(collectionName, ct);

        var docId = list[0].DocumentId;
        await DeleteByDocumentIdAsync(docId, collectionName, ct);

        var points = list.Select(c =>
        {
            var v = new Vector();
            v.Data.AddRange(c.Embedding);

            var point = new PointStruct
            {
                Id      = new PointId { Uuid = Guid.NewGuid().ToString() },
                Vectors = new Qdrant.Client.Grpc.Vectors { Vector = v },
            };

            point.Payload["text"]         = c.Text;
            point.Payload["document_id"]  = c.DocumentId;
            point.Payload["title"]        = c.Title;
            point.Payload["doc_type"]     = c.DocType;
            point.Payload["chunk_index"]  = (long)c.ChunkIndex;

            return point;
        }).ToList();

        await client.UpsertAsync(collectionName, points, cancellationToken: ct);
        logger.LogInformation("Upserted {Count} chunks for document '{DocId}' in '{Collection}'", list.Count, docId, collectionName);
    }

    public async Task<List<VectorChunk>> SearchAsync(
        float[]           queryVector,
        int               topK,
        string            collectionName,
        CancellationToken ct = default)
    {
        try
        {
            var results = await client.SearchAsync(
                collectionName:   collectionName,
                vector:           queryVector,
                limit:            (ulong)topK,
                payloadSelector:  new WithPayloadSelector { Enable = true },
                cancellationToken: ct);

            return results.Select(r => new VectorChunk(
                Id:         GetStr(r.Payload, "chunk_id"),
                Text:       GetStr(r.Payload, "text"),
                Embedding:  Array.Empty<float>(),
                DocumentId: GetStr(r.Payload, "document_id"),
                Title:      GetStr(r.Payload, "title"),
                DocType:    GetStr(r.Payload, "doc_type"),
                ChunkIndex: GetInt(r.Payload, "chunk_index")
            )).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Qdrant search failed in '{Collection}' — returning empty results", collectionName);
            return new List<VectorChunk>();
        }
    }

    public async Task<(int ChunkCount, int DocumentCount)> GetStatsAsync(string collectionName, CancellationToken ct = default)
    {
        try
        {
            var chunkCount = await client.CountAsync(collectionName, cancellationToken: ct);
            var scrollResult = await client.ScrollAsync(collectionName,
                limit: 1000,
                payloadSelector: new WithPayloadSelector { Enable = true },
                cancellationToken: ct);
            var docCount = scrollResult.Result.Select(p => GetStr(p.Payload, "document_id"))
                                              .Distinct()
                                              .Count();
            return ((int)chunkCount, docCount);
        }
        catch
        {
            return (0, 0);
        }
    }

    public async Task ClearAsync(string collectionName, CancellationToken ct = default)
    {
        await client.DeleteCollectionAsync(collectionName, cancellationToken: ct);
        await EnsureCollectionExistsAsync(collectionName, ct);
        logger.LogInformation("Cleared Qdrant collection '{Name}'", collectionName);
    }

    private async Task DeleteByDocumentIdAsync(string documentId, string collectionName, CancellationToken ct)
    {
        var filter = new Filter();
        filter.Must.Add(new Condition
        {
            Field = new FieldCondition
            {
                Key   = "document_id",
                Match = new Match { Keyword = documentId },
            }
        });
        await client.DeleteAsync(collectionName, filter: filter, cancellationToken: ct);
    }

    private static string GetStr(IDictionary<string, Value> payload, string key) =>
        payload.TryGetValue(key, out var v) ? v.StringValue : string.Empty;

    private static int GetInt(IDictionary<string, Value> payload, string key) =>
        payload.TryGetValue(key, out var v) ? (int)v.IntegerValue : 0;
}
