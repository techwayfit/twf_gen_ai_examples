using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwfAiFramework.Core;
using TwfAiFramework.Core.Extensions;
using TwfAiFramework.Core.ValueObjects;
using TwfAiFramework.Nodes.AI;
using TwfAiFramework.Nodes.Control;
using TwfAiFramework.Nodes.Data;
using TwfAiFramework.Nodes.IO;

namespace _007_FakeNewsMisInformationDetector.Services;

/// <summary>
/// Builds and executes the misinformation detection pipeline.
///
/// Pipeline stages:
///   1. ValidateInput       — FilterNode:      ensure article_text is present and within limits
///   2. ExtractClaims       — PromptBuilderNode + LlmNode + OutputParserNode
///   3. SearchEvidence      — AddStep:         loop over claims, query Serper.dev for each
///   4. EvaluateClaims      — PromptBuilderNode + LlmNode (chain-of-thought)
///   5. ParseAndAssemble    — AddStep:         parse JSON verdict, fire SSE complete event
/// </summary>
public class FactCheckWorkflowService(
    ILogger<FactCheckWorkflowService> logger,
    IHttpClientFactory httpClientFactory)
{
    private readonly ILogger<FactCheckWorkflowService> _logger = logger;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    // ── Public entry point ────────────────────────────────────────────────────

    public async Task<WorkflowResult> RunAsync(
        FactCheckBrief brief,
        Func<StageEvent, Task> sendStageAsync,
        Func<CredibilityReport, Task> sendCompleteAsync,
        LlmConfig llmConfig,
        string searchApiKey,
        string searchEndpoint,
        CancellationToken ct = default)
    {
        var httpClient = _httpClientFactory.CreateClient("search");

        var workflow = BuildWorkflow(
            brief,
            sendStageAsync,
            sendCompleteAsync,
            llmConfig,
            searchApiKey,
            searchEndpoint,
            httpClient);

        var input = WorkflowData
            .From("article_text", brief.ArticleText)
            .Set("max_claims", brief.MaxClaims.ToString());

        var context = new WorkflowContext("FakeNewsDetector", _logger);
        return await workflow.RunAsync(input, context, ct);
    }

    // ── Workflow builder ──────────────────────────────────────────────────────

    private Workflow BuildWorkflow(
        FactCheckBrief brief,
        Func<StageEvent, Task> sendStageAsync,
        Func<CredibilityReport, Task> sendCompleteAsync,
        LlmConfig llmConfig,
        string searchApiKey,
        string searchEndpoint,
        HttpClient httpClient)
    {
        var workflow = Workflow.Create("FakeNewsDetector").UseLogger(_logger);

        // ── 1. Validate input ────────────────────────────────────────────────
        workflow.AddNode(
            new FilterNode("ValidateInput")
                .RequireNonEmpty("article_text")
                .MaxLength("article_text", 10_000));

        // ── 2. Extract factual claims ────────────────────────────────────────
        workflow.AddStep("NotifyExtractionStage", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Extracting factual claims from article...", 1, 3));
            return data;
        });

      /*  workflow.AddNode(new PromptBuilderNode(
            name: "ClaimExtractionPrompt",
            promptTemplate: Constants.Prompts.ClaimExtractionPrompt,
            systemTemplate: Constants.Prompts.ClaimExtractionSystemPrompt));

        workflow.AddNode(
            new LlmNode("ClaimExtractionLlm", llmConfig with { MaxTokens = TokenCount.FromValue(600) }),
            NodeOptions.WithRetry(2));*/
        workflow.AddAIPipeline(new AIPipelineConfig
        {
            NodePrefix = "ClaimExtraction",
            Llm = llmConfig with { MaxTokens = TokenCount.FromValue(600)},
            PromptTemplate = Constants.Prompts.ClaimExtractionPrompt,
            SystemTemplate = Constants.Prompts.ClaimExtractionSystemPrompt,
        });

        workflow.AddNode(OutputParserNode.WithMapping("ParseClaims",
            ("claims", "claims")));
        
        
        // ── 3. Search for evidence per claim ─────────────────────────────────
        var sb = new StringBuilder();
        workflow.ForEach("claims", "claim", async (loop) =>
        {
            loop.AddStep("PrepareSearch", async (data, _) =>
            {
                var keyword = data.GetString("__loop_item__") ?? string.Empty;
                data.Set("search_query", keyword).Set("search_results_count", 5);
                return data;
            }).AddNode(new GoogleSearchNode(searchApiKey, httpClient))
            .AddStep("CollectEvidence", async (data, _) =>
            {
                var results = data.Get<List<SearchResultItem>>("search_results")
                              ?? new List<SearchResultItem>();

                if (results.Count == 0)
                {
                    sb.AppendLine("  No relevant search results found.");
                }
                else
                {
                    sb.AppendLine(string.Join(Environment.NewLine, results.Select((r, i) =>
                    {
                        var entry = $"{i+1}. {r.Title} " +
                                    $"{(string.IsNullOrEmpty(r.Description) ? string.Empty : "Snippet: " + r.Description)}" +
                                    $"{(string.IsNullOrEmpty(r.LinkedPage) ? string.Empty : " URL: " + r.LinkedPage)}";
                        return entry;
                    })));
                }
                return data;
            });
        }).AddStep("FinalizeEvidence", async (data, _) =>
        {
            _logger.LogInformation(
                "Evidence collected for claims — {Chars} chars",
                sb.Length);
            return data.Set("claims_with_evidence", sb.ToString());
        });
        
       

        // ── 4. Evaluate claims with chain-of-thought reasoning ───────────────
        workflow.AddStep("NotifyEvaluationStage", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Evaluating claims with chain-of-thought reasoning...", 3, 3));
            return data;
        });

       /* workflow.AddNode(new PromptBuilderNode(
            name: "EvaluationPrompt",
            promptTemplate: Constants.Prompts.EvaluationPrompt,
            systemTemplate: Constants.Prompts.EvaluationSystemPrompt));

        workflow.AddNode(
            new LlmNode("EvaluationLlm", llmConfig with { MaxTokens = TokenCount.Standard }),
            NodeOptions.WithRetry(2));*/

        workflow.AddAIPipeline(new AIPipelineConfig
        {
            NodePrefix = "Evaluation",
            Llm = llmConfig with { MaxTokens = TokenCount.Standard },
            PromptTemplate = Constants.Prompts.EvaluationPrompt,
            SystemTemplate = Constants.Prompts.EvaluationSystemPrompt,
        });
        

        // ── 5. Parse JSON verdict and fire complete event ────────────────────
        workflow.AddStep("ParseAndAssemble", async (data, _) =>
        {
            var rawJson = data.LlmResponse();
            CredibilityReport report;

            try
            {
                rawJson = StripCodeFences(rawJson);
                var dto = JsonSerializer.Deserialize<CredibilityReportDto>(rawJson, JsonOptions)
                          ?? throw new JsonException("Deserialized result was null.");

                report = dto.ToCredibilityReport();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse evaluation JSON — returning fallback report");
                report = new CredibilityReport(
                    OverallCredibilityScore: 0,
                    OverallVerdict: "error",
                    Summary: "The evaluation result could not be parsed. Please try again.",
                    ClaimVerdicts: new List<ClaimVerdict>(),
                    CheckedAt: DateTime.UtcNow);
            }

            await sendCompleteAsync(report);
            return data;
        });

        return workflow;
    }

    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Strips ```json … ``` or ``` … ``` code fences if the model wraps its output.</summary>
    private static string StripCodeFences(string raw)
    {
        var s = raw.Trim();
        if (s.StartsWith("```"))
        {
            var firstNewline = s.IndexOf('\n');
            if (firstNewline > 0) s = s[(firstNewline + 1)..];
            if (s.EndsWith("```")) s = s[..^3];
        }
        return s.Trim();
    }

}

// ── Event / model types ───────────────────────────────────────────────────────

public record StageEvent(string Message, int StageIndex, int TotalStages);

public record CredibilityReport(
    int OverallCredibilityScore,
    string OverallVerdict,
    string Summary,
    List<ClaimVerdict> ClaimVerdicts,
    DateTime CheckedAt);

public record ClaimVerdict(
    string Id,
    string Text,
    string Verdict,
    double Confidence,
    string Reasoning,
    List<EvidenceSource> Sources);

public record EvidenceSource(
    string Title,
    string Url,
    string Snippet);

public class FactCheckBrief
{
    public string ArticleText { get; set; } = string.Empty;
    public int MaxClaims { get; set; } = 5;
}

// ── JSON DTO types (mirrors the LLM JSON output) ─────────────────────────────

public class CredibilityReportDto
{
    [JsonPropertyName("overall_credibility_score")]
    public int OverallCredibilityScore { get; set; }

    [JsonPropertyName("overall_verdict")]
    public string OverallVerdict { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("claim_verdicts")]
    public List<ClaimVerdictDto> ClaimVerdicts { get; set; } = new();

    public CredibilityReport ToCredibilityReport() => new(
        OverallCredibilityScore: OverallCredibilityScore,
        OverallVerdict: OverallVerdict,
        Summary: Summary,
        ClaimVerdicts: ClaimVerdicts.Select(c => c.ToClaimVerdict()).ToList(),
        CheckedAt: DateTime.UtcNow);
}

public class ClaimVerdictDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("verdict")]
    public string Verdict { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = string.Empty;

    [JsonPropertyName("sources")]
    public List<EvidenceSourceDto> Sources { get; set; } = new();

    public ClaimVerdict ToClaimVerdict() => new(
        Id: Id,
        Text: Text,
        Verdict: Verdict,
        Confidence: Confidence,
        Reasoning: Reasoning,
        Sources: Sources.Select(s => s.ToEvidenceSource()).ToList());
}

public class EvidenceSourceDto
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("snippet")]
    public string Snippet { get; set; } = string.Empty;

    public EvidenceSource ToEvidenceSource() => new(Title, Url, Snippet);
}

