using System.Text;
using System.Text.Json;
using TwfAiFramework.Core;
using TwfAiFramework.Nodes.AI;
using TwfAiFramework.Nodes.Control;
using TwfAiFramework.Nodes.Data;
using _003_Automated_NewsLetter.Models;

namespace _003_Automated_NewsLetter.Services;

/// <summary>
/// Builds and executes the multi-stage newsletter generation pipeline using TwfAiFramework.
///
/// Pipeline stages:
///   1. FetchFeeds       — AddStep: fetch top-50 articles from RSS feeds concurrently
///   2. ClusterArticles  — PromptBuilderNode + LlmNode + AddStep(parse): single LLM call classifies all articles
///   3. GroupAndScore    — AddStep: group by cluster, score against subscriber profile
///   4. Branch           — short-circuit if no clusters meet the relevance threshold
///   5. EnrichContent    — AddStep: scrape full text for relevant cluster articles only
///   6. SummarizeClusters— AddStep loop: per-cluster mini-workflow (PromptBuilderNode + LlmNode)
///   7. BuildPrompt      — AddStep: assemble the final newsletter prompt
///   8. GenerateNewsletter— PromptBuilderNode.WithSystem + LlmNode (streaming via SSE)
/// </summary>
public class NewsletterWorkflowService
{
    private readonly ILogger<NewsletterWorkflowService> _logger;
    private readonly IConfiguration _configuration;
    private readonly RssFeedService _rssFeedService;
    private readonly ContentEnrichmentService _enrichmentService;
    private readonly SubscriberProfileService _subscriberProfileService;
    private readonly FeedConfigService _feedConfigService;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public GeneratedNewsletter? LatestNewsletter { get; private set; }

    public NewsletterWorkflowService(
        ILogger<NewsletterWorkflowService> logger,
        IConfiguration configuration,
        RssFeedService rssFeedService,
        ContentEnrichmentService enrichmentService,
        SubscriberProfileService subscriberProfileService,
        FeedConfigService feedConfigService)
    {
        _logger                   = logger;
        _configuration            = configuration;
        _rssFeedService           = rssFeedService;
        _enrichmentService        = enrichmentService;
        _subscriberProfileService = subscriberProfileService;
        _feedConfigService        = feedConfigService;
    }

    // ── Public entry point ────────────────────────────────────────────────────

    public async Task<WorkflowResult> RunPipelineAsync(
        Action<string> onChunk,
        CancellationToken ct = default)
    {
        var apiKey = _configuration["OpenAI:ApiKey"] ?? string.Empty;
        if (string.IsNullOrEmpty(apiKey) || apiKey == "your-openai-api-key-here")
          throw new Exception("OpenAI API key is not configured.");

        var llmConfig = BuildLlmConfig(apiKey);
        var profile   = await _subscriberProfileService.GetProfileAsync();
        var settings  = _feedConfigService.GetSettings();
        var feeds     = await _feedConfigService.GetFeedsAsync();

        var workflow = BuildWorkflow(llmConfig, onChunk, profile, settings, feeds);
        var context  = new WorkflowContext("NewsletterPipeline", _logger);
        var input    = WorkflowData.From("pipeline_start", true);

        var result = await workflow.RunAsync(input, context, ct);

        if (result.IsSuccess)
        {
            var markdown = result.Data.GetString("newsletter_markdown") ?? string.Empty;
            LatestNewsletter = new GeneratedNewsletter
            {
                MarkdownContent = markdown,
                GeneratedAt     = DateTime.UtcNow,
                ClusterCount    = result.Data.Get<List<ClusterSummary>>("cluster_summaries")?.Count ?? 0,
                ArticleCount    = result.Data.Get<List<RawArticle>>("raw_articles")?.Count ?? 0
            };
        }

        return result;
    }

    // ── Workflow builder ──────────────────────────────────────────────────────

    private Workflow BuildWorkflow(
        LlmConfig llmConfig,
        Action<string> onChunk,
        SubscriberProfile profile,
        NewsletterSettings settings,
        List<string> feeds)
    {
        var workflow = Workflow.Create("NewsletterPipeline").UseLogger(_logger);

        // ── Step 1: Fetch articles from all RSS feeds ─────────────────────
        workflow.AddStep("FetchFeeds", async (data, ct) =>
        {
            _logger.LogInformation("Fetching articles from {Count} feeds (max {Max})", feeds.Count, settings.MaxArticles);
            var articles = await _rssFeedService.FetchArticlesAsync(
                feeds, settings.MaxArticles, settings.MaxAgeDays);

            _logger.LogInformation("Fetched {Count} articles", articles.Count);

            if (articles.Count == 0)
                return data.Set("fetch_error", "No articles fetched — check feed configuration.");

            return data
                .Set("raw_articles", articles)
                .Set("article_count", articles.Count);
        });

        // Validate that we got articles before continuing
        workflow.AddNode(new FilterNode("ValidateFetch").RequireNonEmpty("article_count"));

        // ── Step 2a: Build the relevance-filtering prompt ─────────────────
        workflow.AddStep("BuildClusteringPrompt", (data, _) =>
        {
            var articles = data.Get<List<RawArticle>>("raw_articles")!;
            var sb       = new StringBuilder();

            for (int i = 0; i < (articles.Count); i++)
            {
                var a = articles[i];
                sb.AppendLine($"{i + 1}. Title: {a.Title}");
                if (!string.IsNullOrWhiteSpace(a.Snippet))
                    sb.AppendLine($"   Snippet: {a.Snippet[..Math.Min(200, a.Snippet.Length)]}");
                sb.AppendLine($"   URL: {a.Url}");
                sb.AppendLine();
            }

            var interestsSb = new StringBuilder();
            foreach (var (topic, weight) in profile.InterestWeights.OrderByDescending(kv => kv.Value))
                interestsSb.AppendLine($"- {topic} (weight: {weight}/10)");

            return Task.FromResult(data
                .Set("articles_text",     sb.ToString())
                .Set("interests_text",    interestsSb.ToString())
                .Set("article_count_str", articles.Count.ToString()));
        });

        // ── Step 2b: Single LLM call — cluster and annotate all articles ──
        workflow.AddNode(new PromptBuilderNode(
            name:           "ClusteringPrompt",
            promptTemplate: Constants.Prompts.ClusteringUserPrompt,
            systemTemplate: Constants.Prompts.ClusteringSystemPrompt));

        workflow.AddNode(
            new LlmNode("ClusteringLlm", llmConfig with { MaxTokens = 2000 }),
            NodeOptions.WithRetry(2));

        // ── Step 2c: Parse clustering JSON array from llm_response ────────
        workflow.AddStep("ParseClusteredArticles", (data, _) =>
        {
            var json      = data.GetString("llm_response") ?? "[]";
            var extracted = ExtractJsonArray(json);
            List<ClusteredArticle> clustered;
            try
            {
                clustered = JsonSerializer.Deserialize<List<ClusteredArticle>>(extracted, JsonOpts)
                    ?? new List<ClusteredArticle>();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse clustering response as JSON");
                clustered = new List<ClusteredArticle>();
            }

            _logger.LogInformation("Clustering produced {Count} annotated articles", clustered.Count);
            return Task.FromResult(data.Set("clustered_articles", clustered));
        });

        // ── Step 3: Group LLM-filtered articles by topic ──────────────────
        // The LLM already selected only relevant articles, so no keyword scoring needed.
        workflow.AddStep("GroupAndScore", (data, _) =>
        {
            var clustered = data.Get<List<ClusteredArticle>>("clustered_articles")
                            ?? new List<ClusteredArticle>();

            var groups = clustered
                .GroupBy(a => a.Cluster, StringComparer.OrdinalIgnoreCase)
                .Select(g => new TopicCluster
                {
                    Label          = g.Key,
                    Articles       = g.ToList(),
                    RelevanceScore = 10 // LLM already filtered for relevance
                })
                .Take(profile.PreferredSectionCount)
                .ToList();

            _logger.LogInformation(
                "LLM selected {ArticleCount} relevant articles across {TopicCount} topics",
                clustered.Count, groups.Count);

            return Task.FromResult(data
                .Set("ranked_clusters",       groups)
                .Set("relevant_clusters",     groups)
                .Set("has_relevant_clusters", groups.Count > 0));
        });

        // ── Branch: any relevant clusters? ───────────────────────────────
        workflow.Branch(
            condition: d => d.Get<bool>("has_relevant_clusters"),
            trueBranch:    enrichAndGenerate =>
            {
                // ── Step 4: Lazy content enrichment ───────────────────────
                enrichAndGenerate.AddStep("EnrichContent", async (data, ct) =>
                {
                    var relevant = data.Get<List<TopicCluster>>("relevant_clusters")!;
                    _logger.LogInformation("Enriching content for {Count} clusters", relevant.Count);

                    foreach (var cluster in relevant)
                    {
                        cluster.EnrichedContent = await _enrichmentService
                            .EnrichClusterAsync(cluster.Articles.Take(3));
                    }

                    return data.Set("relevant_clusters", relevant);
                });

                // ── Step 5: Per-cluster summarisation loop ─────────────────
                enrichAndGenerate.AddStep("SummarizeClusters", async (data, ct) =>
                {
                    var relevant  = data.Get<List<TopicCluster>>("relevant_clusters")!;
                    var summaries = new List<ClusterSummary>();

                    foreach (var cluster in relevant)
                    {
                        _logger.LogInformation("Summarising cluster: {Label}", cluster.Label);
                        var summary = await SummariseClusterAsync(cluster, llmConfig, default);
                        if (summary is not null)
                            summaries.Add(summary);
                    }

                    return data.Set("cluster_summaries", summaries);
                });

                // ── Step 6: Assemble the final newsletter prompt ───────────
                enrichAndGenerate.AddStep("BuildNewsletterPrompt", (data, _) =>
                {
                    var summaries = data.Get<List<ClusterSummary>>("cluster_summaries")!;
                    var sb        = new StringBuilder();
                    var rank      = 1;

                    foreach (var s in summaries)
                    {
                        sb.AppendLine($"### Section {rank}: {s.Label}");
                        sb.AppendLine($"**Summary:** {s.Summary}");
                        sb.AppendLine("**Key Takeaways:**");
                        foreach (var kt in s.KeyTakeaways)
                            sb.AppendLine($"- {kt}");
                        if (!string.IsNullOrWhiteSpace(s.TopArticleUrl))
                            sb.AppendLine($"**Top Article:** {s.TopArticleUrl}");
                        sb.AppendLine();
                        rank++;
                    }

                    var dateRange = $"{DateTime.UtcNow.AddDays(-7):MMM d} – {DateTime.UtcNow:MMM d, yyyy}";

                    var finalPrompt = Constants.Prompts.NewsletterUserPrompt
                        .Replace("{subscriber_name}",  profile.DisplayName)
                        .Replace("{date_range}",        dateRange)
                        .Replace("{tone}",              profile.Tone)
                        .Replace("{section_count}",     summaries.Count.ToString())
                        .Replace("{sections_content}",  sb.ToString());

                    return Task.FromResult(data
                        .Set("newsletter_user_prompt", finalPrompt)
                        .Set("newsletter_tone",        profile.Tone));
                });

                // ── Step 7: Stream the newsletter ──────────────────────────
                var systemPrompt = Constants.Prompts.NewsletterSystemPrompt
                    .Replace("{tone}", profile.Tone);

                enrichAndGenerate.AddNode(PromptBuilderNode.WithSystem(
                    "NewsletterPrompt",
                    "{{newsletter_user_prompt}}",
                    systemPrompt));

                enrichAndGenerate.AddNode(
                    new LlmNode("NewsletterGenerator", llmConfig with
                    {
                        Stream    = true,
                        OnChunk   = onChunk,
                        MaxTokens = 2000
                    }),
                    NodeOptions.WithRetry(1));

                // Capture the full streamed content as newsletter_markdown
                enrichAndGenerate.AddStep("CaptureNewsletter", (data, _) =>
                {
                    var markdown = data.GetString("llm_response") ?? string.Empty;
                    return Task.FromResult(data.Set("newsletter_markdown", markdown));
                });
            },
            falseBranch: noContent =>
            {
                // ── 3b: No relevant clusters — return a polite empty notice ──
                noContent.AddStep("NoRelevantContent", (data, _) =>
                {
                    var notice = $"# Newsletter — {DateTime.UtcNow:MMM d, yyyy}\n\n" +
                                 $"Hi {profile.DisplayName},\n\n" +
                                 "No articles this week matched your interest profile. " +
                                 "Try lowering your **Minimum Relevance Score** or adding more RSS feeds.\n\n" +
                                 "_The TechWayFit Newsletter Team_";

                    onChunk(notice);
                    return Task.FromResult(data.Set("newsletter_markdown", notice));
                });
            });

        return workflow;
    }

    // ── Per-cluster summarisation mini-workflow ───────────────────────────────

    private async Task<ClusterSummary?> SummariseClusterAsync(
        TopicCluster cluster,
        LlmConfig llmConfig,
        CancellationToken ct)
    {
        try
        {
            var miniWorkflow = Workflow.Create($"ClusterSummarizer_{SanitiseLabel(cluster.Label)}")
                .UseLogger(_logger);

            var topArticleUrl = cluster.Articles.FirstOrDefault()?.Url ?? string.Empty;
            var imageUrl      = cluster.Articles.FirstOrDefault()?.ImageUrl ?? string.Empty;

            miniWorkflow.AddNode(new PromptBuilderNode(
                name:           "ClusterSummaryPrompt",
                promptTemplate: Constants.Prompts.ClusterSummaryUserPrompt,
                systemTemplate: Constants.Prompts.ClusterSummarySystemPrompt));

            miniWorkflow.AddNode(
                new LlmNode("ClusterSummaryLlm", llmConfig with { MaxTokens = 600 }),
                NodeOptions.WithRetry(2));

            miniWorkflow.AddNode(OutputParserNode.WithMapping("ClusterSummaryParser",
                ("summary",       "cluster_summary"),
                ("keyTakeaways",  "cluster_key_takeaways_raw"),
                ("topArticleUrl", "cluster_top_url"),
                ("imageUrl",      "cluster_img_url")));

            var input   = WorkflowData
                .From("cluster_label",         cluster.Label)
                .Set("cluster_articles_text",  cluster.EnrichedContent);
            var context = new WorkflowContext($"ClusterSummarizer_{SanitiseLabel(cluster.Label)}", _logger);
            var result  = await miniWorkflow.RunAsync(input, context, ct);

            if (!result.IsSuccess)
            {
                _logger.LogWarning("Cluster summary failed for {Label}: {Error}", cluster.Label, result.ErrorMessage);
                return null;
            }

            var rawTakeaways = result.Data.Get<List<string>>("cluster_key_takeaways_raw");
            var takeaways = rawTakeaways;//ParseStringList(rawTakeaways);

            return new ClusterSummary
            {
                Label         = cluster.Label,
                Summary       = result.Data.GetString("cluster_summary")   ?? string.Empty,
                KeyTakeaways  = takeaways,
                TopArticleUrl = result.Data.GetString("cluster_top_url")  ?? topArticleUrl,
                ImageUrl      = result.Data.GetString("cluster_img_url")  ?? imageUrl
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception summarising cluster {Label}", cluster.Label);
            return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private LlmConfig BuildLlmConfig(string apiKey)
    {
        var model    = _configuration["OpenAI:Model"]    ?? LlmConfig.OpenAI(apiKey).Model;
        var endpoint = _configuration["OpenAI:Endpoint"] ?? LlmConfig.OpenAI(apiKey).ApiEndpoint;
        return LlmConfig.LmServer(model: model, apiKey: apiKey, apiEndpoint: endpoint);
    }

    /// <summary>Extracts a JSON array from text that may contain surrounding prose.</summary>
    private static string ExtractJsonArray(string text)
    {
        var start = text.IndexOf('[');
        var end   = text.LastIndexOf(']');
        return start >= 0 && end > start ? text[start..(end + 1)] : "[]";
    }

    /// <summary>Parses a JSON string array (from OutputParserNode string value).</summary>
    private static List<string> ParseStringList(string raw)
    {
        raw = raw.Trim();
        if (!raw.StartsWith('[')) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(raw, JsonOpts) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static string SanitiseLabel(string label) =>
        System.Text.RegularExpressions.Regex.Replace(label, @"[^a-zA-Z0-9_]", "_");
}
