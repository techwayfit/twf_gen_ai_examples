using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace _030_RFPComplianceEngine.Services;

public class EmbeddingService(IHttpClientFactory httpClientFactory)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<float[]> EmbedAsync(
        string text,
        string apiKey,
        string model    = "text-embedding-3-small",
        string endpoint = "https://api.openai.com/v1/embeddings",
        CancellationToken ct = default)
    {
        var client  = httpClientFactory.CreateClient("openai");
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new { input = text, model }, options: JsonOpts);

        var response = await client.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);

        var body = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(JsonOpts, ct)
                   ?? throw new InvalidOperationException("Empty embedding response.");

        return body.Data.FirstOrDefault()?.Embedding
               ?? throw new InvalidOperationException("No embedding data in response.");
    }

    public async Task<List<float[]>> EmbedBatchAsync(
        IEnumerable<string> texts,
        string apiKey,
        string model    = "text-embedding-3-small",
        string endpoint = "https://api.openai.com/v1/embeddings",
        CancellationToken ct = default)
    {
        var list = texts.ToList();
        if (list.Count == 0) return new List<float[]>();

        var client  = httpClientFactory.CreateClient("openai");
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new { input = list, model }, options: JsonOpts);

        var response = await client.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);

        var body = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(JsonOpts, ct)
                   ?? throw new InvalidOperationException("Empty embedding response.");

        return body.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding)
            .ToList();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            $"Embedding API error {(int)response.StatusCode}: {body}",
            inner: null,
            statusCode: response.StatusCode);
    }
}

file class EmbeddingResponse
{
    public List<EmbeddingData> Data { get; set; } = new();
}

file class EmbeddingData
{
    public int     Index     { get; set; }
    public float[] Embedding { get; set; } = Array.Empty<float>();
}
