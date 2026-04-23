using System.Text.Json;
using System.Text.Json.Serialization;
using TwfAiFramework.Core;
using TwfAiFramework.Core.Extensions;
using TwfAiFramework.Core.ValueObjects;
using TwfAiFramework.Nodes.AI;
using TwfAiFramework.Nodes.Control;
using TwfAiFramework.Nodes.Data;

namespace _008_PersonalizedChildrenStoryGenerator.Services;

/// <summary>
/// Builds and executes the personalized children's story generation pipeline.
///
/// Pipeline stages:
///   1. ValidateInput      — FilterNode:  ensure child_name, interest, and moral_lesson are present
///   2. NormalizeAgeRange  — AddStep:     map age_range_min to a reading_level label
///   3. GenerateStory      — AIPipeline:  produce an age-appropriate narrative with image prompts
///   4. ParseAndAssemble   — AddStep:     parse JSON output, fire SSE complete event
/// </summary>
public class StoryWorkflowService(ILogger<StoryWorkflowService> logger)
{
    private readonly ILogger<StoryWorkflowService> _logger = logger;

    // ── Public entry point ────────────────────────────────────────────────────

    public async Task<WorkflowResult> RunAsync(
        StoryBrief brief,
        Func<StageEvent, Task> sendStageAsync,
        Func<StoryResult, Task> sendCompleteAsync,
        LlmConfig llmConfig,
        CancellationToken ct = default)
    {
        var workflow = BuildWorkflow(brief, sendStageAsync, sendCompleteAsync, llmConfig);

        var input = WorkflowData
            .From("child_name",    brief.ChildName)
            .Set("interest",       brief.Interest)
            .Set("moral_lesson",   brief.MoralLesson)
            .Set("age_range_min",  brief.AgeRangeMin.ToString())
            .Set("language",       brief.Language)
            .Set("story_length",   brief.StoryLength);

        var context = new WorkflowContext("ChildrenStoryGenerator", _logger);
        return await workflow.RunAsync(input, context, ct);
    }

    // ── Workflow builder ──────────────────────────────────────────────────────

    private Workflow BuildWorkflow(
        StoryBrief brief,
        Func<StageEvent, Task> sendStageAsync,
        Func<StoryResult, Task> sendCompleteAsync,
        LlmConfig llmConfig)
    {
        var workflow = Workflow.Create("ChildrenStoryGenerator").UseLogger(_logger);

        // ── 1. Validate input ────────────────────────────────────────────────
        workflow.AddNode(
            new FilterNode("ValidateInput")
                .RequireNonEmpty("child_name")
                .RequireNonEmpty("interest")
                .RequireNonEmpty("moral_lesson")
                .MaxLength("child_name",    50)
                .MaxLength("interest",      100)
                .MaxLength("moral_lesson",  200));

        // ── 2. Normalise age range → reading level ───────────────────────────
        workflow.AddStep("NormalizeAgeRange", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Building story prompt...", 1, 2));

            var ageMin = int.TryParse(data.GetString("age_range_min"), out var a) ? a : 6;
            var level  = ageMin <= 4 ? "toddler"
                       : ageMin <= 8 ? "early-reader"
                                     : "middle-grade";
            return data.Set("reading_level", level);
        });

        // ── 3. Generate story ────────────────────────────────────────────────
        workflow.AddStep("NotifyGenerateStage", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Generating story...", 2, 2));
            return data;
        });

        workflow.AddAIPipeline(new AIPipelineConfig
        {
            NodePrefix     = "GenerateStory",
            Llm            = llmConfig with { MaxTokens = TokenCount.Extended },
            PromptTemplate = Constants.Prompts.StoryPromptTemplate,
            SystemTemplate = Constants.Prompts.StorySystemPrompt,
        });

        // ── 4. Parse and assemble ────────────────────────────────────────────
        workflow.AddStep("ParseAndAssemble", async (data, _) =>
        {
            var rawJson = StripCodeFences(data.LlmResponse() ?? string.Empty);
            StoryResult result;

            try
            {
                var dto = JsonSerializer.Deserialize<StoryResultDto>(rawJson, JsonOptions)
                          ?? throw new JsonException("Deserialized result was null.");

                result = new StoryResult(
                    Title:          dto.Title,
                    ReadingLevel:   data.GetString("reading_level") ?? "early-reader",
                    StoryText:      dto.StoryText,
                    ImagePrompts:   dto.ImagePrompts,
                    MoralHighlight: dto.MoralHighlight,
                    WordCount:      dto.WordCount,
                    GeneratedAt:    DateTime.UtcNow);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse story JSON — returning raw text fallback");
                result = new StoryResult(
                    Title:          "Your Story",
                    ReadingLevel:   data.GetString("reading_level") ?? "early-reader",
                    StoryText:      rawJson,
                    ImagePrompts:   new List<string>(),
                    MoralHighlight: string.Empty,
                    WordCount:      0,
                    GeneratedAt:    DateTime.UtcNow);
            }

            await sendCompleteAsync(result);
            return data;
        });

        return workflow;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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

public record StoryResult(
    string       Title,
    string       ReadingLevel,
    string       StoryText,
    List<string> ImagePrompts,
    string       MoralHighlight,
    int          WordCount,
    DateTime     GeneratedAt);

public class StoryBrief
{
    public string ChildName    { get; set; } = string.Empty;
    public string Interest     { get; set; } = string.Empty;
    public string MoralLesson  { get; set; } = string.Empty;
    public int    AgeRangeMin  { get; set; } = 6;
    public string Language     { get; set; } = "English";
    public string StoryLength  { get; set; } = "short";
}

// ── JSON DTO types (mirrors the LLM JSON output) ─────────────────────────────

public class StoryResultDto
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("storyText")]
    public string StoryText { get; set; } = string.Empty;

    [JsonPropertyName("imagePrompts")]
    public List<string> ImagePrompts { get; set; } = new();

    [JsonPropertyName("moralHighlight")]
    public string MoralHighlight { get; set; } = string.Empty;

    [JsonPropertyName("wordCount")]
    public int WordCount { get; set; }
}
