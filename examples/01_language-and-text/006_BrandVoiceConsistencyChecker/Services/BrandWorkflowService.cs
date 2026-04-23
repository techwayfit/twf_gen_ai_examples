using System.Text.Json;
using System.Text.Json.Serialization;
using TwfAiFramework.Core;
using TwfAiFramework.Core.Extensions;
using TwfAiFramework.Core.ValueObjects;
using TwfAiFramework.Nodes.AI;
using TwfAiFramework.Nodes.Control;
using TwfAiFramework.Nodes.Data;

namespace _006_BrandVoiceConsistencyChecker.Services;

/// <summary>
/// Builds and executes the brand voice consistency pipeline.
///
/// Pipeline stages:
///   1. ValidateInput       — FilterNode:       ensure copy_text and brand_guidelines are present
///   2. EmbedCopy           — EmbeddingNode:    generate vector for copy_text
///   3. EmbedGuidelines     — EmbeddingNode:    generate vector for brand_guidelines
///   4. ComputeSimilarity   — AddStep:          cosine similarity → similarity_score
///   5. AnalyseCopy         — AIPipeline:       identify tone/vocabulary/style deviations
///   6. ParseViolations     — AddStep:          parse JSON violations list
///   7. GenerateRewrites    — AIPipeline:       produce targeted rewrite suggestions
///   8. ParseAndAssemble    — AddStep:          parse final report, fire SSE complete event
/// </summary>
public class BrandWorkflowService(ILogger<BrandWorkflowService> logger)
{
    private readonly ILogger<BrandWorkflowService> _logger = logger;

    // ── Public entry point ────────────────────────────────────────────────────

    public async Task<WorkflowResult> RunAsync(
        BrandCheckBrief brief,
        Func<StageEvent, Task> sendStageAsync,
        Func<ConsistencyReport, Task> sendCompleteAsync,
        LlmConfig llmConfig,
        string openAiApiKey,
        string embeddingModel,
        CancellationToken ct = default)
    {
        var workflow = BuildWorkflow(brief, sendStageAsync, sendCompleteAsync, llmConfig, openAiApiKey, embeddingModel);

        var input = WorkflowData
            .From("copy_text",        brief.CopyText)
            .Set("brand_guidelines",  brief.BrandGuidelines)
            .Set("brand_name",        brief.BrandName)
            .Set("copy_type",         brief.CopyType)
            .Set("strictness",        brief.Strictness);

        var context = new WorkflowContext("BrandVoiceChecker", _logger);
        return await workflow.RunAsync(input, context, ct);
    }

    // ── Workflow builder ──────────────────────────────────────────────────────

    private Workflow BuildWorkflow(
        BrandCheckBrief brief,
        Func<StageEvent, Task> sendStageAsync,
        Func<ConsistencyReport, Task> sendCompleteAsync,
        LlmConfig llmConfig,
        string openAiApiKey,
        string embeddingModel)
    {
        var workflow = Workflow.Create("BrandVoiceChecker").UseLogger(_logger);

        // ── 1. Validate input ────────────────────────────────────────────────
        workflow.AddNode(
            new FilterNode("ValidateInput")
                .RequireNonEmpty("copy_text")
                .RequireNonEmpty("brand_guidelines")
                .MaxLength("copy_text",       8_000)
                .MaxLength("brand_guidelines", 5_000));

        // ── 2. Embed copy and brand guidelines ───────────────────────────────
        workflow.AddStep("NotifyEmbeddingStage", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Embedding copy and brand guidelines...", 1, 3));
            return data;
        });

        workflow.AddNode(new EmbeddingNode(new EmbeddingConfig
        {
            Provider  = "openai",
            Model     = embeddingModel,
            ApiKey    = openAiApiKey,
            InputKey  = "copy_text",
            OutputKey = "copy_embedding"
        }));

        workflow.AddNode(new EmbeddingNode(new EmbeddingConfig
        {
            Provider  = "openai",
            Model     = embeddingModel,
            ApiKey    = openAiApiKey,
            InputKey  = "brand_guidelines",
            OutputKey = "guidelines_embedding"
        }));

        // ── 3. Compute cosine similarity ─────────────────────────────────────
        workflow.AddStep("ComputeSimilarity", async (data, _) =>
        {
            var copyEmb  = data.Get<float[]>("copy_embedding")       ?? Array.Empty<float>();
            var guideEmb = data.Get<float[]>("guidelines_embedding") ?? Array.Empty<float>();
            var score    = CosineSimilarity(copyEmb, guideEmb);
            return data.Set("similarity_score", $"{score:F2}");
        });

        // ── 4. Analyse copy against guidelines ───────────────────────────────
        workflow.AddStep("NotifyAnalysisStage", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Analysing brand voice deviations...", 2, 3));
            return data;
        });

        workflow.AddAIPipeline(new AIPipelineConfig
        {
            NodePrefix     = "AnalyseCopy",
            Llm            = llmConfig with { MaxTokens = TokenCount.Standard },
            PromptTemplate = Constants.Prompts.AnalysisPrompt,
            SystemTemplate = Constants.Prompts.AnalysisSystemPrompt,
        });

        // ── 5. Parse violation list ──────────────────────────────────────────
        workflow.AddStep("ParseViolations", async (data, _) =>
        {
            var rawJson = StripCodeFences(data.LlmResponse());

            List<ViolationItemDto> violations;
            try
            {
                var analysisDto = JsonSerializer.Deserialize<AnalysisDto>(rawJson, JsonOptions);
                violations = analysisDto?.Violations ?? new List<ViolationItemDto>();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse analysis JSON — proceeding with empty violations");
                violations = new List<ViolationItemDto>();
            }

            // Serialise violations to a JSON string so the rewrite prompt template can inject them
            return data.Set("violations", JsonSerializer.Serialize(violations, JsonOptions));
        });

        // ── 6. Generate rewrite suggestions ─────────────────────────────────
        workflow.AddStep("NotifyRewriteStage", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Generating rewrite suggestions...", 3, 3));
            return data;
        });

        workflow.AddAIPipeline(new AIPipelineConfig
        {
            NodePrefix     = "GenerateRewrites",
            Llm            = llmConfig with { MaxTokens = TokenCount.Standard },
            PromptTemplate = Constants.Prompts.RewritePrompt,
            SystemTemplate = Constants.Prompts.RewriteSystemPrompt,
        });

        // ── 7. Assemble final report ─────────────────────────────────────────
        workflow.AddStep("ParseAndAssemble", async (data, _) =>
        {
            var rawJson = StripCodeFences(data.LlmResponse());
            ConsistencyReport report;

            try
            {
                var dto = JsonSerializer.Deserialize<ConsistencyReportDto>(rawJson, JsonOptions)
                          ?? throw new JsonException("Deserialized result was null.");

                var similarityScore = double.TryParse(
                    data.GetString("similarity_score"),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var s) ? s : 0.0;

                report = dto.ToConsistencyReport(similarityScore);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse rewrite JSON — returning fallback report");
                report = new ConsistencyReport(
                    SimilarityScore:      0.0,
                    OverallRating:        "error",
                    Summary:              "The consistency report could not be generated. Please try again.",
                    Violations:           new List<ViolationItem>(),
                    ApprovedForPublishing: false,
                    CheckedAt:            DateTime.UtcNow);
            }

            await sendCompleteAsync(report);
            return data;
        });

        return workflow;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0) return 0f;

        double dot = 0, normA = 0, normB = 0;
        int len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
        {
            dot   += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return (normA == 0 || normB == 0) ? 0f
            : (float)(dot / (Math.Sqrt(normA) * Math.Sqrt(normB)));
    }

    private static string StripCodeFences(string raw)
    {
        var s = raw.Trim();
        if (s.StartsWith("```"))
        {
            var nl = s.IndexOf('\n');
            if (nl > 0) s = s[(nl + 1)..];
            if (s.EndsWith("```")) s = s[..^3];
        }
        return s.Trim();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}

// ── Event / model types ───────────────────────────────────────────────────────

public record StageEvent(string Message, int StageIndex, int TotalStages);

public record ConsistencyReport(
    double              SimilarityScore,
    string              OverallRating,
    string              Summary,
    List<ViolationItem> Violations,
    bool                ApprovedForPublishing,
    DateTime            CheckedAt);

public record ViolationItem(
    string Category,
    string Severity,
    string Excerpt,
    string Explanation,
    string SuggestedRewrite);

public class BrandCheckBrief
{
    public string CopyText        { get; set; } = string.Empty;
    public string BrandGuidelines { get; set; } = string.Empty;
    public string BrandName       { get; set; } = "Our Brand";
    public string CopyType        { get; set; } = "marketing copy";
    public string Strictness      { get; set; } = "standard";
}

// ── JSON DTO types (mirrors the LLM JSON output) ─────────────────────────────

public class AnalysisDto
{
    [JsonPropertyName("violations")]
    public List<ViolationItemDto> Violations { get; set; } = new();
}

public class ViolationItemDto
{
    [JsonPropertyName("category")]
    public string Category       { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity       { get; set; } = string.Empty;

    [JsonPropertyName("excerpt")]
    public string Excerpt        { get; set; } = string.Empty;

    [JsonPropertyName("explanation")]
    public string Explanation    { get; set; } = string.Empty;

    [JsonPropertyName("suggestedRewrite")]
    public string SuggestedRewrite { get; set; } = string.Empty;
}

public class ConsistencyReportDto
{
    [JsonPropertyName("overallRating")]
    public string OverallRating       { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary             { get; set; } = string.Empty;

    [JsonPropertyName("approvedForPublishing")]
    public bool   ApprovedForPublishing { get; set; }

    [JsonPropertyName("violations")]
    public List<ViolationItemDto> Violations { get; set; } = new();

    public ConsistencyReport ToConsistencyReport(double similarityScore) => new(
        SimilarityScore:       similarityScore,
        OverallRating:         OverallRating,
        Summary:               Summary,
        Violations:            Violations.Select(v => new ViolationItem(
            v.Category, v.Severity, v.Excerpt, v.Explanation, v.SuggestedRewrite)).ToList(),
        ApprovedForPublishing: ApprovedForPublishing,
        CheckedAt:             DateTime.UtcNow);
}
