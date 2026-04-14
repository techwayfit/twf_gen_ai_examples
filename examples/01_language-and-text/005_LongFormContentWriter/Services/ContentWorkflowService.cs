using System.Text;
using TwfAiFramework.Core;
using TwfAiFramework.Nodes.AI;
using TwfAiFramework.Nodes.Control;
using TwfAiFramework.Nodes.Data;
using TwfAiFramework.Nodes.IO;

namespace _005_LongFormContentWriter.Services;

/// <summary>
/// Builds and executes the multi-stage SEO content-authoring pipeline.
///
/// Pipeline stages:
///   1. ValidateInput       — FilterNode: ensure primary_keyword is present
///   2. PrepareSearch       — AddStep: set search_query + search_results_count, fire SSE stage event
///   3. SearchCompetitors   — GoogleSearchNode: queries SerpApi, writes search_results
///   4. NormaliseResearch   — AddStep: format List&lt;SearchResultItem&gt; → competitor_research string
///   5. GenerateOutline     — PromptBuilderNode + LlmNode + OutputParserNode
///   6. SendOutline         — AddStep: fire SSE outline event with structured data
///   7. GenerateDraft       — PromptBuilderNode + LlmNode (streaming) + AddStep capture
///   8. GenerateMetadata    — PromptBuilderNode + LlmNode + OutputParserNode
///   9. Assemble            — AddStep: fire SSE complete event
/// </summary>
public class ContentWorkflowService(
    ILogger<ContentWorkflowService> logger,
    IHttpClientFactory httpClientFactory)
{
    private readonly ILogger<ContentWorkflowService> _logger            = logger;
    private readonly IHttpClientFactory              _httpClientFactory = httpClientFactory;

    // ── Public entry point ────────────────────────────────────────────────────

    /// <summary>
    /// Runs the full content-authoring workflow and fires SSE events via the provided callbacks.
    /// </summary>
    public async Task<WorkflowResult> RunAsync(
        ContentBrief brief,
        Func<StageEvent, Task> sendStageAsync,
        Func<OutlineEvent, Task> sendOutlineAsync,
        Action<string> sendChunkSync,
        Func<ContentPackage, Task> sendCompleteAsync,
        LlmConfig llmConfig,
        string searchApiKey,
        CancellationToken ct = default)
    {
        var httpClient = _httpClientFactory.CreateClient("search");

        var workflow = BuildWorkflow(
            brief,
            sendStageAsync,
            sendOutlineAsync,
            sendChunkSync,
            sendCompleteAsync,
            llmConfig,
            searchApiKey,
            httpClient);

        var input = WorkflowData
            .From("primary_keyword",   brief.PrimaryKeyword)
            .Set("secondary_keywords", brief.SecondaryKeywords ?? string.Empty)
            .Set("target_audience",    brief.TargetAudience ?? "general audience")
            .Set("search_intent",      brief.SearchIntent ?? "informational")
            .Set("tone",               brief.Tone ?? Constants.Tones.Expert)
            .Set("target_word_count",  brief.TargetWordCount.ToString())
            .Set("call_to_action",     brief.CallToAction ?? "learn more");

        var context = new WorkflowContext("LongFormContentWriter", _logger);
        return await workflow.RunAsync(input, context, ct);
    }

    // ── Workflow builder ──────────────────────────────────────────────────────

    private Workflow BuildWorkflow(
        ContentBrief brief,
        Func<StageEvent, Task> sendStageAsync,
        Func<OutlineEvent, Task> sendOutlineAsync,
        Action<string> sendChunkSync,
        Func<ContentPackage, Task> sendCompleteAsync,
        LlmConfig llmConfig,
        string searchApiKey,
        HttpClient httpClient)
    {
        var workflow = Workflow.Create("LongFormContentWriter").UseLogger(_logger);

        // ── 1. Validate input ────────────────────────────────────────────────
        workflow.AddNode(
            new FilterNode("ValidateInput")
                .RequireNonEmpty("primary_keyword")
                .MaxLength("primary_keyword", 200));

        // ── 2. Prepare search ────────────────────────────────────────────────
        // Set search_query and search_results_count for GoogleSearchNode, and fire
        // the first SSE stage event so the UI shows progress immediately.
        workflow.AddStep("PrepareSearch", async (data, _) =>
        {
            await sendStageAsync(new StageEvent(
                "Researching competitor content for your keyword...", 1, 4));

            var keyword = data.GetString("primary_keyword")!;
            return data
                .Set("search_query",         keyword)
                .Set("search_results_count", 10);
        });

        // ── 3. Search competitors via GoogleSearchNode ───────────────────────
        // Reads:  search_query, search_results_count
        // Writes: search_results (List<SearchResultItem>), search_query_used, search_results_count
        workflow.AddNode(
            new GoogleSearchNode(searchApiKey, httpClient),
            NodeOptions.WithRetry(2));

        // ── 4. Normalise research ────────────────────────────────────────────
        // Formats the structured List<SearchResultItem> into a plain-text block that
        // can be injected into the outline and draft prompts.
        workflow.AddStep("NormaliseResearch", (data, _) =>
        {
            var results = data.Get<List<SearchResultItem>>("search_results")
                          ?? new List<SearchResultItem>();

            var query = data.GetString("search_query_used") ?? data.GetString("primary_keyword")!;
            var sb    = new StringBuilder();

            sb.AppendLine($"TOP COMPETITOR ARTICLES FOR \"{query}\":");
            sb.AppendLine();

            var rank = 1;
            foreach (var item in results)
            {
                sb.AppendLine($"{rank}. {item.Title}");
                if (!string.IsNullOrWhiteSpace(item.Description))
                    sb.AppendLine($"   {item.Description}");
                if (!string.IsNullOrWhiteSpace(item.LinkedPage))
                    sb.AppendLine($"   URL: {item.LinkedPage}");
                sb.AppendLine();
                rank++;
            }

            _logger.LogInformation(
                "Research normalised — {Count} results, {Chars} chars",
                results.Count, sb.Length);

            return Task.FromResult(data.Set("competitor_research", sb.ToString()));
        });

        // ── 5. Generate outline ──────────────────────────────────────────────
        workflow.AddStep("NotifyOutlineStage", async (data, _) =>
        {
            await sendStageAsync(new StageEvent(
                "Generating SEO-optimised outline...", 2, 4));
            return data;
        });

        workflow.AddNode(new PromptBuilderNode(
            name:           "OutlinePrompt",
            promptTemplate: Constants.Prompts.OutlinePrompt,
            systemTemplate: Constants.Prompts.OutlineSystemPrompt));

        workflow.AddNode(
            new LlmNode("OutlineLlm", llmConfig with { MaxTokens = 1200 }),
            NodeOptions.WithRetry(2));

        workflow.AddNode(OutputParserNode.WithMapping("ParseOutline",
            ("seo_title",        "seo_title"),
            ("slug",             "slug"),
            ("outline_sections", "outline_sections"),
            ("faq_ideas",        "faq_ideas")));

        // ── 6. Send outline event ────────────────────────────────────────────
        workflow.AddStep("SendOutlineEvent", async (data, _) =>
        {
            var outlineSections = data.Get<List<string>>("outline_sections") ?? new List<string>();
            var faqIdeas        = data.Get<List<string>>("faq_ideas")        ?? new List<string>();

            await sendOutlineAsync(new OutlineEvent(
                SeoTitle:        data.GetString("seo_title") ?? string.Empty,
                Slug:            data.GetString("slug")      ?? string.Empty,
                OutlineSections: outlineSections,
                FaqIdeas:        faqIdeas));

            // Flatten outline into the text that the draft prompt will use
            var outlineText = new StringBuilder();
            foreach (var s in outlineSections)
                outlineText.AppendLine($"- {s}");
            if (faqIdeas.Count > 0)
            {
                outlineText.AppendLine("- H2: Frequently Asked Questions");
                foreach (var faq in faqIdeas)
                    outlineText.AppendLine($"  - {faq}");
            }

            return data.Set("outline_sections_text", outlineText.ToString());
        });

        // ── 7. Generate draft (streaming) ────────────────────────────────────
        workflow.AddStep("NotifyDraftStage", async (data, _) =>
        {
            await sendStageAsync(new StageEvent(
                "Writing long-form article draft...", 3, 4));
            return data;
        });

        workflow.AddNode(new PromptBuilderNode(
            name:           "DraftPrompt",
            promptTemplate: Constants.Prompts.DraftPrompt,
            systemTemplate: Constants.Prompts.DraftSystemPrompt));

        workflow.AddNode(
            new LlmNode("DraftLlm", llmConfig with
            {
                Stream    = true,
                OnChunk   = sendChunkSync,
                MaxTokens = 3500
            }),
            NodeOptions.WithRetry(1));

        workflow.AddStep("CaptureDraft", (data, _) =>
        {
            var draft   = data.GetString("llm_response") ?? string.Empty;
            var preview = draft.Length > 800 ? draft[..800] : draft;
            return Task.FromResult(data
                .Set("article_draft",   draft)
                .Set("article_preview", preview));
        });

        // ── 8. Generate metadata ─────────────────────────────────────────────
        workflow.AddStep("NotifyMetaStage", async (data, _) =>
        {
            await sendStageAsync(new StageEvent(
                "Generating SEO metadata and title variants...", 4, 4));
            return data;
        });

        workflow.AddNode(new PromptBuilderNode(
            name:           "MetaPrompt",
            promptTemplate: Constants.Prompts.MetaPrompt,
            systemTemplate: Constants.Prompts.MetaSystemPrompt));

        workflow.AddNode(
            new LlmNode("MetaLlm", llmConfig with { MaxTokens = 600 }),
            NodeOptions.WithRetry(2));

        workflow.AddNode(OutputParserNode.WithMapping("ParseMeta",
            ("meta_description", "meta_description"),
            ("title_variants",   "title_variants"),
            ("social_preview",   "social_preview")));

        // ── 9. Assemble and send complete event ──────────────────────────────
        workflow.AddStep("AssembleAndComplete", async (data, _) =>
        {
            await sendCompleteAsync(new ContentPackage(
                PrimaryKeyword:  brief.PrimaryKeyword,
                SeoTitle:        data.GetString("seo_title")                    ?? string.Empty,
                Slug:            data.GetString("slug")                         ?? string.Empty,
                OutlineSections: data.Get<List<string>>("outline_sections")     ?? new List<string>(),
                FaqIdeas:        data.Get<List<string>>("faq_ideas")            ?? new List<string>(),
                ArticleMarkdown: data.GetString("article_draft")                ?? string.Empty,
                MetaDescription: data.GetString("meta_description")             ?? string.Empty,
                TitleVariants:   data.Get<List<string>>("title_variants")       ?? new List<string>(),
                SocialPreview:   data.GetString("social_preview")               ?? string.Empty,
                GeneratedAt:     DateTime.UtcNow));

            return data;
        });

        return workflow;
    }
}

// ── Event / model types ───────────────────────────────────────────────────────

public record StageEvent(string Message, int StageIndex, int TotalStages);

public record OutlineEvent(
    string       SeoTitle,
    string       Slug,
    List<string> OutlineSections,
    List<string> FaqIdeas);

public record ContentPackage(
    string       PrimaryKeyword,
    string       SeoTitle,
    string       Slug,
    List<string> OutlineSections,
    List<string> FaqIdeas,
    string       ArticleMarkdown,
    string       MetaDescription,
    List<string> TitleVariants,
    string       SocialPreview,
    DateTime     GeneratedAt);

public class ContentBrief
{
    public string  PrimaryKeyword    { get; set; } = string.Empty;
    public string? SecondaryKeywords  { get; set; }
    public string? TargetAudience    { get; set; }
    public string? SearchIntent      { get; set; }
    public string? Tone              { get; set; }
    public int     TargetWordCount   { get; set; } = 1500;
    public string? CallToAction      { get; set; }
}
