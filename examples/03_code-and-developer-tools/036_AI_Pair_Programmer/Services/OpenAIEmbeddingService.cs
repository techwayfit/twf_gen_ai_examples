using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace _036_AI_Pair_Programmer.Services;

/// <summary>
/// OpenAI-based embedding service using the OpenAI API.
/// </summary>
public class OpenAIEmbeddingService : IEmbeddingService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly string _apiKey;
    private readonly string _embeddingModel;
    private readonly string _endpoint;
    private readonly int _embeddingDimension;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public OpenAIEmbeddingService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;

        _apiKey = configuration["OpenAI:ApiKey"] ?? throw new InvalidOperationException("OpenAI:ApiKey not configured");
        _embeddingModel = configuration["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small";
        _endpoint = configuration["OpenAI:Endpoint"] ?? "https://api.openai.com/v1";
        _embeddingDimension = configuration.GetValue<int>("Embeddings:EmbeddingDimension", 1536);
    }

    public int EmbeddingDimension => _embeddingDimension;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("openai");
        var request = new HttpRequestMessage(HttpMethod.Post, BuildEmbeddingsUrl(_endpoint));

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = _embeddingModel,
            input = text
        }), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var payload = await JsonSerializer.DeserializeAsync<EmbeddingResponse>(stream, JsonOpts, ct)
                      ?? throw new InvalidOperationException("Embedding response was empty.");

        return payload.Data.FirstOrDefault()?.Embedding?.ToArray()
               ?? throw new InvalidOperationException("Embedding vector missing in response.");
    }

    private static string BuildEmbeddingsUrl(string endpoint)
    {
        if (endpoint.EndsWith("/embeddings", StringComparison.OrdinalIgnoreCase)) return endpoint;
        if (endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint[..^"/chat/completions".Length] + "/embeddings";
        }

        return endpoint.TrimEnd('/') + "/embeddings";
    }

    private sealed class EmbeddingResponse
    {
        public List<EmbeddingItem> Data { get; set; } = new();
    }

    private sealed class EmbeddingItem
    {
        public List<float> Embedding { get; set; } = new();
    }
}
