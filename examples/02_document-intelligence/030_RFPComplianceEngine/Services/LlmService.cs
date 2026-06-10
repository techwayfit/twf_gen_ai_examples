using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace _030_RFPComplianceEngine.Services;

public class LlmService(IHttpClientFactory httpClientFactory)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        string apiKey,
        string model     = "gpt-4o-mini",
        string endpoint  = "https://api.openai.com/v1/chat/completions",
        int    maxTokens = 4000,
        CancellationToken ct = default)
    {
        var client  = httpClientFactory.CreateClient("openai");
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new
        {
            model,
            max_tokens = maxTokens,
            messages   = new[]
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

        var result = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(JsonOpts, ct)
                     ?? throw new InvalidOperationException("Empty LLM response.");

        return result.Choices.FirstOrDefault()?.Message?.Content
               ?? throw new InvalidOperationException("No content in LLM response.");
    }
}

file class ChatCompletionResponse
{
    public List<ChatChoice> Choices { get; set; } = new();
}

file class ChatChoice
{
    public ChatMessage? Message { get; set; }
}

file class ChatMessage
{
    public string Content { get; set; } = string.Empty;
}
