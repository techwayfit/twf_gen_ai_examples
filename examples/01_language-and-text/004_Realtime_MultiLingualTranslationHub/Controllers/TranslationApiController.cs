using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using TwfAiFramework.Core;
using TwfAiFramework.Nodes.AI;
using TwfAiFramework.Nodes.Control;
using TwfAiFramework.Nodes.Data;

namespace _004_Realtime_MultiLingualTranslationHub.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TranslationApiController : ControllerBase
{
    private readonly ILogger<TranslationApiController> _logger;
    private readonly IConfiguration _configuration;

    private static readonly Dictionary<string, string> SupportedLanguages = new()
    {
        { "es", "Spanish" },
        { "fr", "French" },
        { "de", "German" },
        { "zh", "Chinese (Simplified)" },
        { "ja", "Japanese" },
        { "ar", "Arabic" },
        { "pt", "Portuguese" },
        { "it", "Italian" },
        { "ko", "Korean" },
        { "hi", "Hindi" },
        { "nl", "Dutch" },
        { "ru", "Russian" },
        {"or", "Oriya"},
        {"en", "English"}
    };

    public TranslationApiController(ILogger<TranslationApiController> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    [HttpPost("translate")]
    public async Task Translate([FromBody] TranslationRequest request, CancellationToken ct)
    {
        Response.Headers["Content-Type"]      = "text/event-stream";
        Response.Headers["Cache-Control"]     = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        async Task SendAsync(string evt, object data)
        {
            var json = JsonSerializer.Serialize(data, jsonOpts);
            await Response.WriteAsync($"event: {evt}\ndata: {json}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        if (string.IsNullOrWhiteSpace(request.SourceText))
        {
            await SendAsync("error", new { error = Constants.Messages.EmptySourceText });   return;
        }

        if (request.SourceText.Length > 5000)
        {
            await SendAsync("error", new { error = Constants.Messages.TextTooLong });        return;
        }

        if (!SupportedLanguages.TryGetValue(request.TargetLanguage, out var targetLanguageName))
        {
            await SendAsync("error", new { error = Constants.Messages.UnsupportedLanguage }); return;
        }

        var apiKey = _configuration["OpenAI:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            await SendAsync("error", new { error = Constants.Messages.OpenApiKeyNotConfigured }); return;
        }

        var domain        = string.IsNullOrWhiteSpace(request.Domain) ? "general" : request.Domain.ToLower();
        var glossaryHints = GetGlossaryHints(domain);

        try
        {
            await SendAsync("processing", new { message = "Running translation pipeline…" });

            var llmConfig = BuildLlmConfig(apiKey);
            var workflow  = BuildTranslationWorkflow(llmConfig);

            var input = WorkflowData.From("source_text", request.SourceText)
                .Set("target_language_code", request.TargetLanguage)
                .Set("target_language_name", targetLanguageName)
                .Set("domain",         domain)
                .Set("glossary_hints", glossaryHints);

            var context = new WorkflowContext("TranslationHub", _logger);
            var result  = await workflow.RunAsync(input, context);

            if (!result.IsSuccess)
            {
                _logger.LogError("Translation workflow failed: {Error}", result.ErrorMessage);
                await SendAsync("error", new { error = result.ErrorMessage ?? Constants.Messages.TranslationFailed });
                return;
            }

            var translation     = result.Data.GetString("translation")     ?? string.Empty;
            var sourceLanguage  = result.Data.GetString("source_language") ?? "auto-detected";
            var culturalNotes   = result.Data.GetString("cultural_notes")  ?? string.Empty;
            var flaggedTerms    = result.Data.GetString("flagged_terms")   ?? string.Empty;
            var backTranslation = result.Data.GetString("back_translation") ?? string.Empty;
            var qualityScore    = result.Data.Get<int>("quality_score");

            var qualityLabel = qualityScore switch
            {
                >= 90 => "Excellent",
                >= 75 => "Good",
                >= 50 => "Refined",
                _     => "Review"
            };

            // Stream translation word-by-word
            var tokens = translation.Split(' ');
            foreach (var token in tokens)
            {
                if (ct.IsCancellationRequested) return;
                await SendAsync("token", token + " ");
                await Task.Delay(30, ct);
            }

            // Final complete event with all metadata
            await SendAsync("complete", new TranslationResponse
            {
                Translation       = translation,
                SourceLanguage    = sourceLanguage,
                TargetLanguageCode = request.TargetLanguage,
                Domain            = domain,
                QualityScore      = qualityScore,
                QualityLabel      = qualityLabel,
                BackTranslation   = backTranslation,
                FlaggedTerms      = flaggedTerms,
                CulturalNotes     = culturalNotes,
                Timestamp         = DateTime.UtcNow
            });
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during translation");
            try { await SendAsync("error", new { error = Constants.Messages.UnexpectedError }); } catch { /* response may be gone */ }
        }
    }

    [HttpGet("languages")]
    public IActionResult GetLanguages()
    {
        var languages = SupportedLanguages.Select(kv => new { code = kv.Key, name = kv.Value });
        return Ok(languages);
    }

    private LlmConfig BuildLlmConfig(string apiKey)
    {
        var defaults = LlmConfig.OpenAI(apiKey);
        var model = _configuration["OpenAI:Model"] ?? defaults.Model;
        var endpoint = _configuration["OpenAI:Endpoint"] ?? defaults.ApiEndpoint;
        return LlmConfig.LmServer(model: model, apiKey: apiKey, apiEndpoint: endpoint);
    }

    private Workflow BuildTranslationWorkflow(LlmConfig llmConfig)
    {
        var workflow = Workflow.Create("TranslationHub").UseLogger(_logger);

        // 1. Validate input
        workflow.AddNode(
            new FilterNode("ValidateInput")
                .RequireNonEmpty("source_text")
                .MaxLength("source_text", 5000));

        // 2. Chain-of-thought translation
        workflow.AddNode(new PromptBuilderNode(
            name: "TranslationPrompt",
            promptTemplate: Constants.Prompts.TranslationPrompt,
            systemTemplate: Constants.Prompts.TranslationSystemPrompt));

        workflow.AddNode(
            new LlmNode("TranslationLlm", llmConfig with { MaxTokens = 1500 }),
            NodeOptions.WithRetry(2));

        workflow.AddNode(OutputParserNode.WithMapping("ParseTranslation",
            ("source_language", "source_language"),
            ("translation", "translation"),
            ("cultural_notes", "cultural_notes"),
            ("flagged_terms", "flagged_terms")));

        // 3. Back-translation quality validation
        workflow.AddNode(new PromptBuilderNode(
            name: "BackTranslationPrompt",
            promptTemplate: Constants.Prompts.BackTranslationPrompt,
            systemTemplate: Constants.Prompts.BackTranslationSystemPrompt));

        workflow.AddNode(
            new LlmNode("BackTranslationLlm", llmConfig with { MaxTokens = 800 }),
            NodeOptions.WithRetry(2));

        workflow.AddNode(OutputParserNode.WithMapping("ParseBackTranslation",
            ("back_translation", "back_translation"),
            ("quality_score", "quality_score")));

        // 4. Branch: refine if quality < threshold
        var threshold = _configuration.GetValue<int>("Translation:QualityRefinementThreshold", 75);

        Action<Workflow> refineFlow = flow =>
        {
            flow.AddNode(new PromptBuilderNode(
                name: "RefinementPrompt",
                promptTemplate: Constants.Prompts.RefinementPrompt,
                systemTemplate: Constants.Prompts.RefinementSystemPrompt));

            flow.AddNode(
                new LlmNode("RefinementLlm", llmConfig with { MaxTokens = 1500 }),
                NodeOptions.WithRetry(2));

            flow.AddNode(OutputParserNode.WithMapping("ParseRefinement",
                ("translation", "translation")));
        };

        Action<Workflow> acceptFlow = flow =>
        {
            flow.AddStep("AcceptTranslation", (data, _) =>
                Task.FromResult(data.Set("accepted", true)));
        };

        workflow.Branch(
            condition: data => data.Get<int>("quality_score") < threshold,
            trueBranch: refineFlow,
            falseBranch: acceptFlow);

        return workflow;
    }

    private static string GetGlossaryHints(string domain) => domain switch
    {
        "legal"     => Constants.GlossaryHints.Legal,
        "medical"   => Constants.GlossaryHints.Medical,
        "technical" => Constants.GlossaryHints.Technical,
        "finance"   => Constants.GlossaryHints.Finance,
        "marketing" => Constants.GlossaryHints.Marketing,
        "education" => Constants.GlossaryHints.Education,
        "ecommerce" => Constants.GlossaryHints.Ecommerce,
        _           => Constants.GlossaryHints.General
    };
}

public class TranslationRequest
{
    public string SourceText { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public string Domain { get; set; } = "general";
}

public class TranslationResponse
{
    public string Translation { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = string.Empty;
    public string TargetLanguageCode { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public int QualityScore { get; set; }
    public string QualityLabel { get; set; } = string.Empty;
    public string BackTranslation { get; set; } = string.Empty;
    public string FlaggedTerms { get; set; } = string.Empty;
    public string CulturalNotes { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
