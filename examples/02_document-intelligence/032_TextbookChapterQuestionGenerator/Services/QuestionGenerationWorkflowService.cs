using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TwfAiFramework.Core;
using TwfAiFramework.Core.Extensions;
using TwfAiFramework.Nodes.Control;
using TwfAiFramework.Nodes.Data;

namespace _032_TextbookChapterQuestionGenerator.Services;

/// <summary>
/// Builds and executes the textbook question generation pipeline.
///
/// Pipeline stages:
///   1. ValidateInput        — FilterNode:  ensure chapter text is non-empty and counts are valid
///   2. AnalyzeContent       — AddStep:     call LLM to extract key concepts and Bloom's level map
///   3. GenerateInParallel   — AddStep:     fan out four LLM calls concurrently (MCQ, short-answer, essay, T/F)
///   4. AssembleResult       — AddStep:     merge all question types and fire SSE complete event
/// </summary>
public class QuestionGenerationWorkflowService(
    ILogger<QuestionGenerationWorkflowService> logger,
    LlmService                                 llmService)
{
    // ── Public entry point ────────────────────────────────────────────────────

    public async Task<WorkflowResult> RunAsync(
        GenerationInput                    request,
        Func<StageEvent, Task>             sendStageAsync,
        Func<QuestionBankResult, Task>     sendCompleteAsync,
        string                             apiKey,
        string                             llmModel,
        string                             llmEndpoint,
        CancellationToken                  ct = default)
    {
        var workflow = BuildWorkflow(
            request,
            sendStageAsync,
            sendCompleteAsync,
            apiKey,
            llmModel,
            llmEndpoint,
            ct);

        var input = WorkflowData
            .From("chapter_text",        request.ChapterText)
            .Set("subject",              request.Subject)
            .Set("mcq_count",            request.McqCount.ToString())
            .Set("short_answer_count",   request.ShortAnswerCount.ToString())
            .Set("essay_count",          request.EssayCount.ToString())
            .Set("true_false_count",     request.TrueFalseCount.ToString())
            .Set("word_problem_count",   request.WordProblemCount.ToString())
            .Set("bloom_levels",         string.Join(", ", request.BloomLevels))
            .Set("difficulty",           request.Difficulty);

        var context = new WorkflowContext("TextbookQuestionGenerator", logger);
        return await workflow.RunAsync(input, context, ct);
    }

    // ── Workflow builder ──────────────────────────────────────────────────────

    private Workflow BuildWorkflow(
        GenerationInput                request,
        Func<StageEvent, Task>         sendStageAsync,
        Func<QuestionBankResult, Task> sendCompleteAsync,
        string                         apiKey,
        string                         llmModel,
        string                         llmEndpoint,
        CancellationToken              ct = default)
    {
        var workflow = Workflow.Create("TextbookQuestionGenerator").UseLogger(logger);

        // ── 1. Validate input ────────────────────────────────────────────────
        workflow.AddNode(
            new FilterNode("ValidateInput")
                .RequireNonEmpty("chapter_text")
                .MaxLength("chapter_text", 40_000));

        // ── 2. Analyse chapter content ───────────────────────────────────────
        workflow.AddStep("AnalyzeContent", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Analysing chapter content...", 1, 3));

            var chapterText = data.GetString("chapter_text") ?? string.Empty;
            var subject     = data.GetString("subject")       ?? "General";
            var truncated   = chapterText.Length > 15_000 ? chapterText[..15_000] : chapterText;

            var prompt = Constants.Prompts.ContentAnalysisPrompt
                .Replace("{{chapter_text}}", truncated);

            try
            {
                var json = await llmService.CompleteAsync(
                    Constants.Prompts.ContentAnalysisSystemPrompt.Replace("{{subject}}", subject),
                    prompt,
                    apiKey,
                    llmModel,
                    llmEndpoint,
                    maxTokens: 800,
                    ct);

                data.Set("content_analysis", StripCodeFences(json));
                logger.LogInformation("Content analysis complete");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Content analysis failed — using minimal fallback");
                data.Set("content_analysis", @"{""topics"":[],""key_terms"":[],""bloom_levels"":[],""summary"":""""}");
            }

            return data;
        });

        // ── 3. Generate all question types in parallel ───────────────────────
        workflow.AddStep("GenerateInParallel", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Generating questions in parallel...", 2, 3));

            var chapterText     = data.GetString("chapter_text")    ?? string.Empty;
            var contentAnalysis = data.GetString("content_analysis") ?? "{}";
            var bloomLevels     = data.GetString("bloom_levels")     ?? "Remember, Understand";
            var difficulty      = data.GetString("difficulty")       ?? "mixed";
            var subject         = data.GetString("subject")          ?? "General";

            var truncated = chapterText.Length > 12_000 ? chapterText[..12_000] : chapterText;

            // Build tasks for each enabled question type
            var tasks = new List<Task>();

            if (request.McqCount > 0)
                tasks.Add(GenerateMcqsAsync(data, truncated, contentAnalysis, bloomLevels, difficulty, subject,
                    request.McqCount, apiKey, llmModel, llmEndpoint, ct));

            if (request.ShortAnswerCount > 0)
                tasks.Add(GenerateShortAnswersAsync(data, truncated, contentAnalysis, bloomLevels, difficulty, subject,
                    request.ShortAnswerCount, apiKey, llmModel, llmEndpoint, ct));

            if (request.EssayCount > 0)
                tasks.Add(GenerateEssaysAsync(data, truncated, contentAnalysis, bloomLevels, difficulty, subject,
                    request.EssayCount, apiKey, llmModel, llmEndpoint, ct));

            if (request.TrueFalseCount > 0)
                tasks.Add(GenerateTrueFalseAsync(data, truncated, contentAnalysis, bloomLevels, difficulty, subject,
                    request.TrueFalseCount, apiKey, llmModel, llmEndpoint, ct));

            if (request.WordProblemCount > 0)
                tasks.Add(GenerateWordProblemsAsync(data, truncated, contentAnalysis, bloomLevels, difficulty, subject,
                    request.WordProblemCount, apiKey, llmModel, llmEndpoint, ct));

            await Task.WhenAll(tasks);
            logger.LogInformation("Parallel question generation complete");
            return data;
        });

        // ── 4. Assemble and emit the final result ────────────────────────────
        workflow.AddStep("AssembleResult", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Assembling question bank...", 3, 3));

            var mcqs         = data.Get<List<McqQuestion>>("mcqs")                   ?? new();
            var shortAnswers = data.Get<List<ShortAnswerQuestion>>("short_answers")   ?? new();
            var essays       = data.Get<List<EssayQuestion>>("essays")               ?? new();
            var trueFalse    = data.Get<List<TrueFalseQuestion>>("true_false")       ?? new();
            var wordProblems = data.Get<List<WordProblemQuestion>>("word_problems")  ?? new();

            var allQuestions = mcqs.Cast<object>()
                .Concat(shortAnswers)
                .Concat(essays)
                .Concat(trueFalse)
                .Concat(wordProblems)
                .ToList();

            var bloomCounts = allQuestions
                .Select(q => q switch
                {
                    McqQuestion m          => m.BloomLevel,
                    ShortAnswerQuestion s  => s.BloomLevel,
                    EssayQuestion e        => e.BloomLevel,
                    TrueFalseQuestion t    => t.BloomLevel,
                    WordProblemQuestion w  => w.BloomLevel,
                    _                      => "Unknown"
                })
                .GroupBy(l => l)
                .ToDictionary(g => g.Key, g => g.Count());

            var result = new QuestionBankResult(
                Mcqs:          mcqs,
                ShortAnswers:  shortAnswers,
                Essays:        essays,
                TrueFalse:     trueFalse,
                WordProblems:  wordProblems,
                TotalQuestions: allQuestions.Count,
                CountsByType: new()
                {
                    ["mcq"]          = mcqs.Count,
                    ["short_answer"] = shortAnswers.Count,
                    ["essay"]        = essays.Count,
                    ["true_false"]   = trueFalse.Count,
                    ["word_problem"] = wordProblems.Count,
                },
                CountsByBloomLevel: bloomCounts,
                GeneratedAt: DateTime.UtcNow);

            await sendCompleteAsync(result);
            return data;
        });

        return workflow;
    }

    // ── Individual generation helpers ─────────────────────────────────────────

    private async Task GenerateMcqsAsync(
        WorkflowData data,
        string chapter, string analysis, string bloomLevels, string difficulty, string subject,
        int count, string apiKey, string model, string endpoint, CancellationToken ct)
    {
        try
        {
            var prompt = BuildPrompt(Constants.Prompts.McqPrompt,
                chapter, analysis, bloomLevels, difficulty, count);

            var json = await llmService.CompleteAsync(
                Constants.Prompts.McqSystemPrompt.Replace("{{subject}}", subject), prompt,
                apiKey, model, endpoint, maxTokens: 3000, ct);

            var parsed = ParseQuestions<McqQuestionsWrapper>(json);
            data.Set("mcqs", parsed?.Questions ?? new());
            logger.LogInformation("Generated {Count} MCQs", parsed?.Questions?.Count ?? 0);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MCQ generation failed");
            data.Set("mcqs", new List<McqQuestion>());
        }
    }

    private async Task GenerateShortAnswersAsync(
        WorkflowData data,
        string chapter, string analysis, string bloomLevels, string difficulty, string subject,
        int count, string apiKey, string model, string endpoint, CancellationToken ct)
    {
        try
        {
            var prompt = BuildPrompt(Constants.Prompts.ShortAnswerPrompt,
                chapter, analysis, bloomLevels, difficulty, count);

            var json = await llmService.CompleteAsync(
                Constants.Prompts.ShortAnswerSystemPrompt.Replace("{{subject}}", subject), prompt,
                apiKey, model, endpoint, maxTokens: 3000, ct);

            var parsed = ParseQuestions<ShortAnswerQuestionsWrapper>(json);
            data.Set("short_answers", parsed?.Questions ?? new());
            logger.LogInformation("Generated {Count} short-answer questions", parsed?.Questions?.Count ?? 0);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Short-answer generation failed");
            data.Set("short_answers", new List<ShortAnswerQuestion>());
        }
    }

    private async Task GenerateEssaysAsync(
        WorkflowData data,
        string chapter, string analysis, string bloomLevels, string difficulty, string subject,
        int count, string apiKey, string model, string endpoint, CancellationToken ct)
    {
        try
        {
            var prompt = BuildPrompt(Constants.Prompts.EssayPrompt,
                chapter, analysis, bloomLevels, difficulty, count);

            var json = await llmService.CompleteAsync(
                Constants.Prompts.EssaySystemPrompt.Replace("{{subject}}", subject), prompt,
                apiKey, model, endpoint, maxTokens: 2000, ct);

            var parsed = ParseQuestions<EssayQuestionsWrapper>(json);
            data.Set("essays", parsed?.Questions ?? new());
            logger.LogInformation("Generated {Count} essay prompts", parsed?.Questions?.Count ?? 0);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Essay generation failed");
            data.Set("essays", new List<EssayQuestion>());
        }
    }

    private async Task GenerateTrueFalseAsync(
        WorkflowData data,
        string chapter, string analysis, string bloomLevels, string difficulty, string subject,
        int count, string apiKey, string model, string endpoint, CancellationToken ct)
    {
        try
        {
            var prompt = BuildPrompt(Constants.Prompts.TrueFalsePrompt,
                chapter, analysis, bloomLevels, difficulty, count);

            var json = await llmService.CompleteAsync(
                Constants.Prompts.TrueFalseSystemPrompt.Replace("{{subject}}", subject), prompt,
                apiKey, model, endpoint, maxTokens: 2000, ct);

            var parsed = ParseQuestions<TrueFalseQuestionsWrapper>(json);
            data.Set("true_false", parsed?.Questions ?? new());
            logger.LogInformation("Generated {Count} true/false statements", parsed?.Questions?.Count ?? 0);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "True/false generation failed");
            data.Set("true_false", new List<TrueFalseQuestion>());
        }
    }

    private async Task GenerateWordProblemsAsync(
        WorkflowData data,
        string chapter, string analysis, string bloomLevels, string difficulty, string subject,
        int count, string apiKey, string model, string endpoint, CancellationToken ct)
    {
        try
        {
            var prompt = BuildPrompt(Constants.Prompts.WordProblemPrompt,
                chapter, analysis, bloomLevels, difficulty, count);

            var json = await llmService.CompleteAsync(
                Constants.Prompts.WordProblemSystemPrompt.Replace("{{subject}}", subject), prompt,
                apiKey, model, endpoint, maxTokens: 4000, ct);

            var parsed = ParseQuestions<WordProblemsWrapper>(json);
            data.Set("word_problems", parsed?.Questions ?? new());
            logger.LogInformation("Generated {Count} word problems", parsed?.Questions?.Count ?? 0);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Word problem generation failed");
            data.Set("word_problems", new List<WordProblemQuestion>());
        }
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    private static string BuildPrompt(
        string template,
        string chapter, string analysis, string bloomLevels, string difficulty, int count) =>
        template
            .Replace("{{chapter_text}}",    chapter)
            .Replace("{{content_analysis}}", analysis)
            .Replace("{{bloom_levels}}",    bloomLevels)
            .Replace("{{difficulty}}",      difficulty)
            .Replace("{{count}}",           count.ToString());

    private T? ParseQuestions<T>(string json)
    {
        var stripped  = StripCodeFences(json);
        var sanitized = FixJsonEscapes(stripped);
        try
        {
            return JsonSerializer.Deserialize<T>(sanitized, JsonOpts);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse questions JSON");
            return default;
        }
    }

    // Compiled regex: matches a backslash NOT followed by a valid JSON escape character
    // Valid: " \ / b f n r t  and  uXXXX (4 hex digits)
    private static readonly Regex InvalidEscapeRegex =
        new(@"\\(?![""\\\//bfnrt]|u[0-9a-fA-F]{4})", RegexOptions.Compiled);

    /// <summary>
    /// LLMs sometimes emit raw LaTeX inside JSON strings (e.g. \displaystyle, \dfrac).
    /// Those backslash sequences are invalid in JSON.  This method doubles any backslash
    /// that is not already a valid JSON escape so the string survives deserialization
    /// as a literal backslash in the resulting value.
    /// </summary>
    private static string FixJsonEscapes(string json) =>
        InvalidEscapeRegex.Replace(json, _ => "\\\\");

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

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
    };
}

// ── Domain types ──────────────────────────────────────────────────────────────

public record McqQuestion(
    [property: JsonPropertyName("question")]       string       Question,
    [property: JsonPropertyName("correct_answer")] string       CorrectAnswer,
    [property: JsonPropertyName("distractors")]    List<string> Distractors,
    [property: JsonPropertyName("bloom_level")]    string       BloomLevel,
    [property: JsonPropertyName("difficulty")]     string       Difficulty);

public record ShortAnswerQuestion(
    [property: JsonPropertyName("question")]         string Question,
    [property: JsonPropertyName("model_answer")]     string ModelAnswer,
    [property: JsonPropertyName("marking_criteria")] string MarkingCriteria,
    [property: JsonPropertyName("bloom_level")]      string BloomLevel,
    [property: JsonPropertyName("difficulty")]       string Difficulty);

public record EssayQuestion(
    [property: JsonPropertyName("prompt")]               string Prompt,
    [property: JsonPropertyName("rubric")]               string Rubric,
    [property: JsonPropertyName("suggested_word_count")] int    SuggestedWordCount,
    [property: JsonPropertyName("bloom_level")]          string BloomLevel,
    [property: JsonPropertyName("difficulty")]           string Difficulty);

public record TrueFalseQuestion(
    [property: JsonPropertyName("statement")]     string Statement,
    [property: JsonPropertyName("is_true")]       bool   IsTrue,
    [property: JsonPropertyName("justification")] string Justification,
    [property: JsonPropertyName("bloom_level")]   string BloomLevel,
    [property: JsonPropertyName("difficulty")]    string Difficulty);

public record WordProblemQuestion(
    [property: JsonPropertyName("problem")]        string       Problem,
    [property: JsonPropertyName("hint")]           string       Hint,
    [property: JsonPropertyName("solution_steps")] List<string> SolutionSteps,
    [property: JsonPropertyName("final_answer")]   string       FinalAnswer,
    [property: JsonPropertyName("svg_diagram")]    string       SvgDiagram,
    [property: JsonPropertyName("bloom_level")]    string       BloomLevel,
    [property: JsonPropertyName("difficulty")]     string       Difficulty);

public record QuestionBankResult(
    List<McqQuestion>           Mcqs,
    List<ShortAnswerQuestion>   ShortAnswers,
    List<EssayQuestion>         Essays,
    List<TrueFalseQuestion>     TrueFalse,
    List<WordProblemQuestion>   WordProblems,
    int                         TotalQuestions,
    Dictionary<string, int>     CountsByType,
    Dictionary<string, int>     CountsByBloomLevel,
    DateTime                    GeneratedAt);

public record StageEvent(string Message, int StageIndex, int TotalStages);

public class GenerationInput
{
    public string       ChapterText       { get; set; } = string.Empty;
    public string       Subject           { get; set; } = "General";
    public int          McqCount          { get; set; } = 5;
    public int          ShortAnswerCount  { get; set; } = 3;
    public int          EssayCount        { get; set; } = 1;
    public int          TrueFalseCount    { get; set; } = 5;
    public int          WordProblemCount  { get; set; } = 0;
    public List<string> BloomLevels       { get; set; } = new() { "Remember", "Understand", "Apply" };
    public string       Difficulty        { get; set; } = "mixed";
}

// ── JSON wrapper types ────────────────────────────────────────────────────────

file class McqQuestionsWrapper
{
    [JsonPropertyName("questions")] public List<McqQuestion>? Questions { get; set; }
}

file class ShortAnswerQuestionsWrapper
{
    [JsonPropertyName("questions")] public List<ShortAnswerQuestion>? Questions { get; set; }
}

file class EssayQuestionsWrapper
{
    [JsonPropertyName("questions")] public List<EssayQuestion>? Questions { get; set; }
}

file class TrueFalseQuestionsWrapper
{
    [JsonPropertyName("questions")] public List<TrueFalseQuestion>? Questions { get; set; }
}

file class WordProblemsWrapper
{
    [JsonPropertyName("questions")] public List<WordProblemQuestion>? Questions { get; set; }
}
