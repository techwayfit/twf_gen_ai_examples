using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using _036_AI_Pair_Programmer.Models;

namespace _036_AI_Pair_Programmer.Services;

public sealed class QdrantVectorStoreService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<QdrantVectorStoreService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly string _baseUrl = (configuration["Qdrant:BaseUrl"] ?? string.Empty).TrimEnd('/');
    private readonly string _apiKey = configuration["Qdrant:ApiKey"] ?? string.Empty;
    private readonly string _collection = string.IsNullOrWhiteSpace(configuration["Qdrant:CollectionName"])
        ? "repo-code-index"
        : configuration["Qdrant:CollectionName"]!;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_baseUrl);

    public async Task UpsertAsync(string repoPath, IReadOnlyList<IndexedChunk> chunks, CancellationToken ct)
    {
        if (!IsConfigured || chunks.Count == 0)
        {
            return;
        }

        await EnsureCollectionAsync(chunks[0].Embedding.Length, ct);

        var client = CreateClient();
        foreach (var batch in Batch(chunks, 64))
        {
            var points = batch.Select(c => new
            {
                id = BuildPointId(repoPath, c.FilePath, c.StartLine, c.EndLine),
                vector = c.Embedding,
                payload = new
                {
                    repo_path = NormalizePath(repoPath),
                    file_path = c.FilePath,
                    snippet = c.Text,
                    start_line = c.StartLine,
                    end_line = c.EndLine
                }
            }).ToArray();

            using var request = new HttpRequestMessage(HttpMethod.Put, $"{_baseUrl}/collections/{_collection}/points?wait=true");
            request.Content = new StringContent(JsonSerializer.Serialize(new { points }, JsonOpts), Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
        }

        logger.LogInformation("Upserted {Count} chunks into Qdrant collection {Collection}", chunks.Count, _collection);
    }

    public async Task<List<RetrievedChunk>> SearchAsync(string repoPath, float[] vector, int topK, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return new List<RetrievedChunk>();
        }

        var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/collections/{_collection}/points/search");

        var body = new
        {
            vector,
            limit = Math.Clamp(topK, 1, 20),
            with_payload = true,
            with_vector = false,
            filter = new
            {
                must = new object[]
                {
                    new
                    {
                        key = "repo_path",
                        match = new { value = NormalizePath(repoPath) }
                    }
                }
            }
        };

        request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Qdrant search failed with status {StatusCode}", response.StatusCode);
            return new List<RetrievedChunk>();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("result", out var resultElement) || resultElement.ValueKind != JsonValueKind.Array)
        {
            return new List<RetrievedChunk>();
        }

        var output = new List<RetrievedChunk>();
        foreach (var item in resultElement.EnumerateArray())
        {
            if (!item.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var filePath = payload.TryGetProperty("file_path", out var f) ? f.GetString() ?? string.Empty : string.Empty;
            var snippet = payload.TryGetProperty("snippet", out var s) ? s.GetString() ?? string.Empty : string.Empty;
            var startLine = payload.TryGetProperty("start_line", out var sl) && sl.TryGetInt32(out var slv) ? slv : 1;
            var endLine = payload.TryGetProperty("end_line", out var el) && el.TryGetInt32(out var elv) ? elv : startLine;
            var score = item.TryGetProperty("score", out var sc) && sc.TryGetDouble(out var sv) ? sv : 0;

            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(snippet))
            {
                continue;
            }

            output.Add(new RetrievedChunk(filePath, snippet, startLine, endLine, score));
        }

        return output;
    }

    private async Task EnsureCollectionAsync(int vectorSize, CancellationToken ct)
    {
        var client = CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Put, $"{_baseUrl}/collections/{_collection}");
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            vectors = new
            {
                size = vectorSize,
                distance = "Cosine"
            }
        }, JsonOpts), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest && body.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient("qdrant");
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            client.DefaultRequestHeaders.Remove("api-key");
            client.DefaultRequestHeaders.Add("api-key", _apiKey);
        }

        return client;
    }

    private static string BuildPointId(string repoPath, string filePath, int startLine, int endLine)
    {
        var input = $"{NormalizePath(repoPath)}::{filePath}:{startLine}:{endLine}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes[..16]).ToLowerInvariant();
    }

    private static string NormalizePath(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).Replace('\\', '/');

    private static IEnumerable<IReadOnlyList<T>> Batch<T>(IReadOnlyList<T> items, int batchSize)
    {
        for (var i = 0; i < items.Count; i += batchSize)
        {
            yield return items.Skip(i).Take(batchSize).ToList();
        }
    }
}
