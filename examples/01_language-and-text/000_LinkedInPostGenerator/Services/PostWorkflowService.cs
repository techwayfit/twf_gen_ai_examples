using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TwfAiFramework.Core;
using TwfAiFramework.Core.Extensions;
using TwfAiFramework.Core.ValueObjects;
using TwfAiFramework.Nodes.AI;
using TwfAiFramework.Nodes.Control;
using TwfAiFramework.Nodes.Data;
using TwfAiFramework.Nodes.IO;

namespace _000_LinkedInPostGenerator.Services;

/// <summary>
/// Builds and executes the LinkedIn post generation pipeline.
///
/// Pipeline stages:
///   1. ValidateInput    — FilterNode:          ensure topic and role are present
///   2. GeneratePost     — AddAIPipeline:       draft post with role/audience/profile context
///   3. PrepareAdjust    — AddStep:             compute length delta, build adjustment instruction
///   4. AdjustLength     — AddAIPipeline:       shorten or expand post to hit max_chars
///   5. FormatAndDeliver — AddStep:             attach hashtags, fire SSE complete event
/// </summary>
public class PostWorkflowService(
    ILogger<PostWorkflowService> logger,
    ProfileService profileService)
{
    private readonly ILogger<PostWorkflowService> _logger = logger;
    private readonly ProfileService _profileService = profileService;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ── Public entry point ────────────────────────────────────────────────────

    public async Task<WorkflowResult> RunAsync(
        PostBrief brief,
        Func<StageEvent, Task> sendStageAsync,
        Func<GeneratedPost, Task> sendCompleteAsync,
        LlmConfig llmConfig,
        CancellationToken ct = default)
    {
        var profile  = _profileService.Load();
        var workflow = BuildWorkflow(brief, profile, sendStageAsync, sendCompleteAsync, llmConfig);

        var relatedLinksSection = brief.RelatedLinks.Count > 0
            ? "RELATED LINKS TO EMBED:\n" + string.Join("\n", brief.RelatedLinks.Select(l => $"- {l}"))
            : string.Empty;

        var additionalContextSection = string.IsNullOrWhiteSpace(brief.AdditionalContext)
            ? string.Empty
            : $"ADDITIONAL CONTEXT:\n{brief.AdditionalContext}";

        var input = WorkflowData
            .From("topic",                   brief.Topic)
            .Set("current_role",             brief.CurrentRole)
            .Set("target_audience",          brief.TargetAudience)
            .Set("max_chars",                brief.MaxChars.ToString())
            .Set("related_links_section",    relatedLinksSection)
            .Set("additional_context_section", additionalContextSection);

        var context = new WorkflowContext("LinkedInPostGenerator", _logger);
        return await workflow.RunAsync(input, context, ct);
    }

    // ── Workflow builder ──────────────────────────────────────────────────────

    private Workflow BuildWorkflow(
        PostBrief brief,
        AuthorProfile profile,
        Func<StageEvent, Task> sendStageAsync,
        Func<GeneratedPost, Task> sendCompleteAsync,
        LlmConfig llmConfig)
    {
        var systemPrompt = _profileService.BuildSystemPrompt(profile);
        var workflow     = Workflow.Create("LinkedInPostGenerator").UseLogger(_logger);

        // ── 1. Validate input ────────────────────────────────────────────────
        workflow.AddNode(
            new FilterNode("ValidateInput")
                .RequireNonEmpty("topic")
                .RequireNonEmpty("current_role")
                .MaxLength("topic", 2_000));

        // ── 2. Generate draft post ───────────────────────────────────────────
        workflow.AddStep("NotifyGenerationStage", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Drafting your LinkedIn post...", 1, 2));
            return data;
        });

        var generationSystemPrompt = systemPrompt + "\n\n" + Constants.Prompts.GenerationSystemPrompt;

        workflow.AddAIPipeline(new AIPipelineConfig
        {
            NodePrefix     = "GeneratePost",
            Llm            = llmConfig with { MaxTokens = TokenCount.FromValue(800) },
            PromptTemplate = Constants.Prompts.GenerationPrompt,
            SystemTemplate = generationSystemPrompt,
        });

        // ── 3. Prepare length adjustment ─────────────────────────────────────
        workflow.AddStep("PrepareAdjustment", async (data, _) =>
        {
            var rawPost  = (data.LlmResponse() ?? string.Empty).Trim();
            var maxChars = brief.MaxChars;
            var delta    = rawPost.Length - maxChars;

            string instruction;
            if (delta <= 0)
            {
                instruction = $"The post is within the target length ({rawPost.Length} / {maxChars} characters). " +
                              "Return it exactly as written — do not change a single word.";
            }
            else
            {
                instruction = $"The post is {delta} characters too long ({rawPost.Length} / {maxChars} characters). " +
                              $"Shorten it to fit within {maxChars} characters while keeping the hook, " +
                              "core message, and call to action intact.";
            }

            return data
                .Set("raw_post",         rawPost)
                .Set("raw_post_length",  rawPost.Length.ToString())
                .Set("adjustment_instruction", instruction);
        });

        // ── 4. Adjust length ─────────────────────────────────────────────────
        workflow.AddStep("NotifyAdjustmentStage", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Polishing post to target length...", 2, 2));
            return data;
        });

        workflow.AddAIPipeline(new AIPipelineConfig
        {
            NodePrefix     = "AdjustLength",
            Llm            = llmConfig with { MaxTokens = TokenCount.FromValue(800) },
            PromptTemplate = Constants.Prompts.AdjustmentPrompt,
            SystemTemplate = Constants.Prompts.AdjustmentSystemPrompt,
        });

        // ── 5. Format output and deliver ─────────────────────────────────────
        workflow.AddStep("FormatAndDeliver", async (data, _) =>
        {
            var finalPost = ConvertMarkdownToLinkedIn((data.LlmResponse() ?? string.Empty).Trim());
            var hashtags  = SuggestHashtags(brief.Topic, brief.CurrentRole, brief.TargetAudience);

            var result = new GeneratedPost(
                Post:               finalPost,
                CharCount:          finalPost.Length,
                HashtagSuggestions: hashtags,
                GeneratedAt:        DateTime.UtcNow);

            await sendCompleteAsync(result);
            return data;
        });

        return workflow;
    }

    // ── Hashtag suggestion helper ─────────────────────────────────────────────

    private static List<string> SuggestHashtags(string topic, string role, string audience)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Role-based tags
        switch (role.ToLower())
        {
            case "developer" or "senior-developer":
                tags.Add("#softwareengineering"); tags.Add("#coding"); break;
            case "tech-lead":
                tags.Add("#techleadership"); tags.Add("#softwareengineering"); break;
            case "architect":
                tags.Add("#softwarearchitecture"); tags.Add("#systemdesign"); break;
            case "engineering-manager":
                tags.Add("#engineeringmanagement"); tags.Add("#leadership"); break;
            case "vp-engineering" or "cto":
                tags.Add("#techleadership"); tags.Add("#cto"); break;
            case "product-manager":
                tags.Add("#productmanagement"); tags.Add("#product"); break;
            case "data-scientist":
                tags.Add("#datascience"); tags.Add("#machinelearning"); break;
            case "devops-engineer":
                tags.Add("#devops"); tags.Add("#cloudcomputing"); break;
        }

        // Audience-based tags
        switch (audience.ToLower())
        {
            case "engineers" or "developers":
                tags.Add("#developers"); break;
            case "hiring-managers":
                tags.Add("#hiring"); tags.Add("#recruitment"); break;
            case "job-seekers":
                tags.Add("#jobsearch"); tags.Add("#careers"); break;
            case "startup-founders":
                tags.Add("#startups"); tags.Add("#entrepreneurship"); break;
        }

        // Topic keyword tags
        var keywords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ai"]           = "#artificialintelligence",
            ["llm"]          = "#llm",
            ["cloud"]        = "#cloudcomputing",
            ["kubernetes"]   = "#kubernetes",
            ["docker"]       = "#docker",
            ["react"]        = "#reactjs",
            ["typescript"]   = "#typescript",
            ["python"]       = "#python",
            ["rust"]         = "#rustlang",
            ["go"]           = "#golang",
            ["microservice"] = "#microservices",
            ["api"]          = "#api",
            ["grpc"]         = "#grpc",
            ["security"]     = "#cybersecurity",
            ["leadership"]   = "#leadership",
            ["agile"]        = "#agile",
            ["career"]       = "#careeradvice",
        };

        foreach (var (keyword, tag) in keywords)
        {
            if (topic.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                tags.Add(tag);
        }

        tags.Add("#linkedin");

        return tags.Take(8).ToList();
    }

    // Converts **bold** → Unicode Mathematical Bold Sans-Serif so LinkedIn preserves it.
    // Also strips *italic* markers and leftover code fences.
    private static string ConvertMarkdownToLinkedIn(string text)
    {
        // Bold: **text** → 𝗯𝗼𝗹𝗱
        text = Regex.Replace(text, @"\*\*(.+?)\*\*", m => ToUnicodeBold(m.Groups[1].Value));
        // Italic: *text* → strip markers (Unicode italic is rarely needed on LinkedIn)
        text = Regex.Replace(text, @"\*(.+?)\*", m => m.Groups[1].Value);
        // Strip any leftover markdown headers (### Heading → HEADING in bold)
        text = Regex.Replace(text, @"^#{1,6}\s+(.+)$", m => ToUnicodeBold(m.Groups[1].Value), RegexOptions.Multiline);
        return StripCodeFences(text);
    }

    private static string ToUnicodeBold(string input)
    {
        var sb = new StringBuilder(input.Length * 2);
        foreach (var c in input)
        {
            if      (c >= 'A' && c <= 'Z') sb.Append(char.ConvertFromUtf32(0x1D5D4 + (c - 'A')));
            else if (c >= 'a' && c <= 'z') sb.Append(char.ConvertFromUtf32(0x1D5EE + (c - 'a')));
            else if (c >= '0' && c <= '9') sb.Append(char.ConvertFromUtf32(0x1D7EC + (c - '0')));
            else sb.Append(c);
        }
        return sb.ToString();
    }

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

public record GeneratedPost(
    string Post,
    int CharCount,
    List<string> HashtagSuggestions,
    DateTime GeneratedAt);

public class PostBrief
{
    public string Topic             { get; set; } = string.Empty;
    public string CurrentRole       { get; set; } = string.Empty;
    public string TargetAudience    { get; set; } = string.Empty;
    public int    MaxChars          { get; set; } = 1300;
    public List<string> RelatedLinks       { get; set; } = new();
    public string AdditionalContext { get; set; } = string.Empty;
}
