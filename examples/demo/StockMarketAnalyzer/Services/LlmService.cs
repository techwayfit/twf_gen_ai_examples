using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace _StockMarketAnalyzer.Services;

/// <summary>
/// Wraps the OpenAI API for single-turn JSON generation tasks.
/// Supports both Chat Completions API and the newer Responses API format.
/// </summary>
public class LlmService(IHttpClientFactory httpClientFactory)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Sends a system + user prompt and returns the assistant's text response.
    /// </summary>
    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        string apiKey,
        string model     = "gpt-4o-mini",
        string endpoint  = "https://api.openai.com/v1/chat/completions",
        int    maxTokens = 2000,
        CancellationToken ct = default)
    {
        var client  = httpClientFactory.CreateClient("openai");
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new
        {
            model,
            input   = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt   },
            },
        }, options: JsonOpts);

        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"LLM API error {(int)response.StatusCode}: {errorBody}",
                inner: null,
                statusCode: response.StatusCode);
        }

        var json = await response.Content.ReadAsStringAsync(ct);

        // Try Chat Completions format first: { "choices": [{ "message": { "content": "..." } }] }
        try
        {
            var chatResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(json, JsonOpts);
            var content = chatResponse?.Choices?.FirstOrDefault()?.Message?.Content;
            if (!string.IsNullOrEmpty(content))
                return content;
        }
        catch { /* Not Chat Completions format, try Responses format */ }

        // Try Responses API format: { "output": [{ "content": [{ "type": "output_text", "text": "..." }] }] }
        try
        {
            var responsesApi = JsonSerializer.Deserialize<ResponsesApiFormat>(json, JsonOpts);
            var content = responsesApi?.Output?
                .SelectMany(o => o.Content ?? new())
                .OfType<OutputTextBlock>()
                .FirstOrDefault()?.Text;
            if (!string.IsNullOrEmpty(content))
                return content;
        }
        catch { /* Neither format matched */ }

        throw new InvalidOperationException("Unable to extract content from LLM response. Check API endpoint and model configuration.");
    }
}

// ── Chat Completions API response DTO ──────────────────────────────────────

file class ChatCompletionResponse
{
    public List<ChatChoice>? Choices { get; set; }
}

file class ChatChoice
{
    public ChatMessage? Message { get; set; }
}

file class ChatMessage
{
    public string Content { get; set; } = string.Empty;
}

// ── Responses API format DTOs ──────────────────────────────────────────────

file class ResponsesApiFormat
{
    public List<OutputItem>? Output { get; set; }
}

file class OutputItem
{
    public string? Type { get; set; }
    public List<OutputContentBlock>? Content { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(OutputTextBlock), "output_text")]
file class OutputContentBlock
{
}

file class OutputTextBlock : OutputContentBlock
{
    public string? Text { get; set; }
}
