using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace _036_AI_Pair_Programmer.Services;

public sealed class LlmService(IHttpClientFactory httpClientFactory)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<float[]> EmbedAsync(
        string text,
        string apiKey,
        string embeddingModel,
        string endpoint,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("openai");
        var request = new HttpRequestMessage(HttpMethod.Post, BuildEmbeddingsUrl(endpoint));

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = embeddingModel,
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

    public async Task<string> ChatAsync(
        string systemPrompt,
        string userPrompt,
        string apiKey,
        string chatModel,
        string endpoint,
        int maxTokens,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("openai");
        var request = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionsUrl(endpoint));

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = chatModel,
            max_tokens = maxTokens,
            temperature = 0.2,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        }), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var payload = await JsonSerializer.DeserializeAsync<ChatResponse>(stream, JsonOpts, ct)
                      ?? throw new InvalidOperationException("Chat response was empty.");

        var content = payload.Choices.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("No message content returned from chat completion.");
        }

        return content;
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

    private static string BuildChatCompletionsUrl(string endpoint)
    {
        if (endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return endpoint;
        return endpoint.TrimEnd('/') + "/chat/completions";
    }

    private sealed class EmbeddingResponse
    {
        public List<EmbeddingItem> Data { get; set; } = new();
    }

    private sealed class EmbeddingItem
    {
        public List<float> Embedding { get; set; } = new();
    }

    private sealed class ChatResponse
    {
        public List<Choice> Choices { get; set; } = new();
    }

    private sealed class Choice
    {
        public Message Message { get; set; } = new();
    }

    private sealed class Message
    {
        public string Content { get; set; } = string.Empty;
    }
}
