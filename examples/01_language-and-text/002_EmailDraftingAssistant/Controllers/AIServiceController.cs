using Microsoft.AspNetCore.Mvc;

namespace _002_EmailDraftingAssistant.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AIServiceController : ControllerBase
{
    private readonly ILogger<AIServiceController> _logger;
    private readonly IConfiguration _configuration;

    public AIServiceController(ILogger<AIServiceController> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Generates a draft email based on the provided request details.
    /// </summary>
    [HttpPost("draft")]
    public async Task<IActionResult> DraftEmail([FromBody] EmailDraftRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Context))
            {
                return BadRequest(new { error = Constants.Messages.EmptyPrompt });
            }

            var apiKey = _configuration["OpenAI:ApiKey"];
            if (string.IsNullOrEmpty(apiKey) || apiKey == "your-openai-api-key-here")
            {
                return StatusCode(500, new { error = Constants.Messages.OpenApiKeyNotConfigured });
            }

            // Build the prompt
            var prompt = Constants.Prompts.EmailDraftPrompt
                .Replace("{{context}}", request.Context)
                .Replace("{{tone}}", request.Tone ?? "professional")
                .Replace("{{recipient}}", request.Recipient ?? "the recipient");

            // Call OpenAI API
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var body = new
            {
                model = "gpt-4o-mini",
                messages = new[]
                {
                    new { role = "system", content = Constants.Prompts.EmailDraftSystemPrompt },
                    new { role = "user", content = prompt }
                },
                max_tokens = 1024,
                temperature = 0.7
            };

            var response = await http.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", body);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("OpenAI API error: {StatusCode} {Error}", response.StatusCode, errorContent);
                return StatusCode(500, new { error = Constants.Messages.FailedToProcessRequest });
            }

            var result = await response.Content.ReadFromJsonAsync<OpenAIResponse>();
            var draft = result?.Choices?.FirstOrDefault()?.Message?.Content
                        ?? Constants.Messages.RequestCouldNotProcessed;

            return Ok(new EmailDraftResponse
            {
                Draft = draft,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating email draft");
            return StatusCode(500, new { error = "An unexpected error occurred." });
        }
    }

    /// <summary>
    /// Returns the health status of the AI service.
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", service = Constants.AppName, timestamp = DateTime.UtcNow });
    }
}

// ---------- Request / Response models ----------

public class EmailDraftRequest
{
    public string Context { get; set; } = string.Empty;
    public string? Recipient { get; set; }
    public string? Tone { get; set; }
}

public class EmailDraftResponse
{
    public string Draft { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

// Minimal OpenAI response shape
public class OpenAIResponse
{
    public List<Choice>? Choices { get; set; }
}

public class Choice
{
    public MessageContent? Message { get; set; }
}

public class MessageContent
{
    public string? Content { get; set; }
}
