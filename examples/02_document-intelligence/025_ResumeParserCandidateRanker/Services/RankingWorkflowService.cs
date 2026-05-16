using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwfAiFramework.Core;
using TwfAiFramework.Core.Extensions;
using TwfAiFramework.Nodes.Control;
using TwfAiFramework.Nodes.Data;

namespace _025_ResumeParserCandidateRanker.Services;

/// <summary>
/// Builds and executes the resume ranking pipeline.
///
/// Pipeline stages:
///   1. ValidateInput        — FilterNode:  ensure job description is non-empty
///   2. ParseProfiles        — AddStep:     call LLM per resume to extract structured profiles
///   3. EmbedAndScore        — AddStep:     embed job description + profiles, compute cosine similarity
///   4. RankCandidates       — AddStep:     sort by score, apply threshold filter
///   5. GenerateQuestions    — AddStep:     LLM per top-N candidate to generate interview questions
///   6. AssembleResult       — AddStep:     build final payload and fire SSE complete event
/// </summary>
public class RankingWorkflowService(
    ILogger<RankingWorkflowService> logger,
    EmbeddingService                embeddingService,
    SimilarityService               similarityService,
    LlmService                      llmService)
{
    // ── Public entry point ────────────────────────────────────────────────────

    public async Task<WorkflowResult> RunAsync(
        RankingInput                  request,
        Func<StageEvent, Task>        sendStageAsync,
        Func<RankingResult, Task>     sendCompleteAsync,
        string                        apiKey,
        string                        embeddingModel,
        string                        embeddingEndpoint,
        string                        llmModel,
        string                        llmEndpoint,
        CancellationToken             ct = default)
    {
        var workflow = BuildWorkflow(
            request,
            sendStageAsync,
            sendCompleteAsync,
            apiKey,
            embeddingModel,
            embeddingEndpoint,
            llmModel,
            llmEndpoint,
            ct);

        var input = WorkflowData
            .From("job_description",       request.JobDescription)
            .Set("resume_inputs",          request.Resumes)
            .Set("top_n",                  request.TopN.ToString())
            .Set("similarity_threshold",   request.SimilarityThreshold.ToString("F4"));

        var context = new WorkflowContext("ResumeRanker", logger);
        return await workflow.RunAsync(input, context, ct);
    }

    // ── Workflow builder ──────────────────────────────────────────────────────

    private Workflow BuildWorkflow(
        RankingInput              request,
        Func<StageEvent, Task>    sendStageAsync,
        Func<RankingResult, Task> sendCompleteAsync,
        string                    apiKey,
        string                    embeddingModel,
        string                    embeddingEndpoint,
        string                    llmModel,
        string                    llmEndpoint,
        CancellationToken         ct = default)
    {
        var workflow = Workflow.Create("ResumeRanker").UseLogger(logger);

        // ── 1. Validate input ────────────────────────────────────────────────
        workflow.AddNode(
            new FilterNode("ValidateInput")
                .RequireNonEmpty("job_description")
                .MaxLength("job_description", 20_000));

        // ── 2. Parse structured profiles from resume texts ───────────────────
        workflow.AddStep("ParseProfiles", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Parsing resume profiles...", 1, 3));

            var resumes  = data.Get<List<ResumeInput>>("resume_inputs") ?? new();
            var profiles = new List<ParsedProfile>();

            foreach (var resume in resumes)
            {
                try
                {
                    var prompt = Constants.Prompts.ProfileExtractionPrompt
                        .Replace("{{resume_text}}", resume.Text.Length > 12_000
                            ? resume.Text[..12_000]
                            : resume.Text);

                    var json = await llmService.CompleteAsync(
                        Constants.Prompts.ProfileExtractionSystemPrompt,
                        prompt,
                        apiKey,
                        llmModel,
                        llmEndpoint,
                        maxTokens: 1500,
                        ct);

                    var profile = ParseProfileJson(json, resume.FileName);
                    profiles.Add(profile);
                    logger.LogInformation("Parsed profile for '{FileName}': {Name}",
                        resume.FileName, profile.Name);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to parse profile for '{FileName}' — using fallback",
                        resume.FileName);
                    profiles.Add(FallbackProfile(resume.FileName));
                }
            }

            data.Set("parsed_profiles", profiles);
            return data;
        });

        // ── 3. Embed job description + profiles, compute cosine scores ───────
        workflow.AddStep("EmbedAndScore", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Scoring candidates against job requirements...", 2, 3));

            var jobDesc  = data.GetString("job_description") ?? string.Empty;
            var profiles = data.Get<List<ParsedProfile>>("parsed_profiles") ?? new();

            // Embed job description
            var jobVector = await embeddingService.EmbedAsync(
                jobDesc, apiKey, embeddingModel, embeddingEndpoint, ct);

            // Build a single rich text string per candidate for embedding
            var profileTexts = profiles.Select(BuildProfileText).ToList();

            // Batch-embed all profiles in one API call
            List<float[]> profileVectors;
            if (profileTexts.Count > 0)
            {
                profileVectors = await embeddingService.EmbedBatchAsync(
                    profileTexts, apiKey, embeddingModel, embeddingEndpoint, ct);
            }
            else
            {
                profileVectors = new List<float[]>();
            }

            // Compute scores
            var scored = profiles
                .Zip(profileVectors, (p, v) => (
                    Profile: p,
                    Score:   similarityService.CosineSimilarity(jobVector, v)))
                .ToList();

            data.Set("scored_candidates", scored);
            logger.LogInformation("Scored {Count} candidate(s)", scored.Count);
            return data;
        });

        // ── 4. Rank and filter candidates ────────────────────────────────────
        workflow.AddStep("RankCandidates", async (data, _) =>
        {
            var scored = data
                .Get<List<(ParsedProfile Profile, float Score)>>("scored_candidates") ?? new();

            float threshold = float.TryParse(
                data.GetString("similarity_threshold"),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var t) ? t : 0f;

            var ranked = scored
                .Where(x => x.Score >= threshold)
                .OrderByDescending(x => x.Score)
                .Select((x, i) => new ScoredCandidate(i + 1, x.Score, x.Profile, new()))
                .ToList();

            data.Set("ranked_candidates", ranked);
            logger.LogInformation("Ranked {Count} candidate(s) above threshold {Threshold:F2}",
                ranked.Count, threshold);
            return data;
        });

        // ── 5. Generate interview questions for top-N candidates ─────────────
        workflow.AddStep("GenerateQuestions", async (data, _) =>
        {
            await sendStageAsync(
                new StageEvent("Generating interview questions for top candidates...", 3, 3));

            var ranked  = data.Get<List<ScoredCandidate>>("ranked_candidates") ?? new();
            int topN    = int.TryParse(data.GetString("top_n"), out var n) ? n : 5;
            var jobDesc = data.GetString("job_description") ?? string.Empty;

            var withQuestions = new List<ScoredCandidate>();

            for (int i = 0; i < ranked.Count; i++)
            {
                var candidate = ranked[i];

                if (i < topN)
                {
                    try
                    {
                        var questions = await GenerateQuestionsAsync(
                            candidate.Profile, jobDesc, apiKey, llmModel, llmEndpoint, ct);

                        withQuestions.Add(candidate with { InterviewQuestions = questions });
                        logger.LogInformation("Generated questions for candidate #{Rank}: {Name}",
                            candidate.Rank, candidate.Profile.Name);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex,
                            "Failed to generate questions for candidate #{Rank} — skipping", candidate.Rank);
                        withQuestions.Add(candidate);
                    }
                }
                else
                {
                    withQuestions.Add(candidate);
                }
            }

            data.Set("final_candidates", withQuestions);
            return data;
        });

        // ── 6. Assemble and emit the final result ─────────────────────────────
        workflow.AddStep("AssembleResult", async (data, _) =>
        {
            var final  = data.Get<List<ScoredCandidate>>("final_candidates") ?? new();
            var resumes = data.Get<List<ResumeInput>>("resume_inputs") ?? new();

            var result = new RankingResult(
                RankedCandidates:          final,
                TotalCandidatesEvaluated:  resumes.Count,
                ShortlistedCount:          final.Count,
                EvaluatedAt:               DateTime.UtcNow);

            await sendCompleteAsync(result);
            return data;
        });

        return workflow;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ParsedProfile ParseProfileJson(string json, string fileName)
    {
        json = StripCodeFences(json);
        try
        {
            var dto = JsonSerializer.Deserialize<ProfileDto>(json, JsonOpts);
            if (dto is null) return FallbackProfile(fileName);

            return new ParsedProfile(
                FileName:      fileName,
                Name:          dto.Name      ?? Path.GetFileNameWithoutExtension(fileName),
                Email:         dto.Email     ?? string.Empty,
                Phone:         dto.Phone     ?? string.Empty,
                Location:      dto.Location  ?? string.Empty,
                Summary:       dto.Summary   ?? string.Empty,
                Skills:        dto.Skills    ?? new(),
                Experience:    (dto.Experience ?? new())
                    .Select(e => new WorkExperience(
                        e.Title    ?? string.Empty,
                        e.Company  ?? string.Empty,
                        e.Duration ?? string.Empty,
                        e.Summary  ?? string.Empty))
                    .ToList(),
                EducationList: (dto.Education ?? new())
                    .Select(e => new Education(
                        e.Degree      ?? string.Empty,
                        e.Institution ?? string.Empty,
                        e.Year        ?? string.Empty))
                    .ToList());
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse profile JSON for '{FileName}'", fileName);
            return FallbackProfile(fileName);
        }
    }

    private async Task<List<string>> GenerateQuestionsAsync(
        ParsedProfile     profile,
        string            jobDesc,
        string            apiKey,
        string            model,
        string            endpoint,
        CancellationToken ct)
    {
        var expText = profile.Experience.Count > 0
            ? string.Join("\n", profile.Experience
                .Select(e => $"- {e.Title} at {e.Company} ({e.Duration}): {e.Summary}"))
            : "No prior experience listed.";

        var prompt = Constants.Prompts.InterviewQuestionsPrompt
            .Replace("{{job_description}}",   jobDesc.Length > 5_000 ? jobDesc[..5_000] : jobDesc)
            .Replace("{{candidate_name}}",    profile.Name)
            .Replace("{{candidate_skills}}",  string.Join(", ", profile.Skills))
            .Replace("{{candidate_experience}}", expText);

        var json = await llmService.CompleteAsync(
            Constants.Prompts.InterviewQuestionsSystemPrompt,
            prompt,
            apiKey,
            model,
            endpoint,
            maxTokens: 800,
            ct);

        json = StripCodeFences(json);
        try
        {
            var dto = JsonSerializer.Deserialize<QuestionsWrapper>(json, JsonOpts);
            return dto?.Questions ?? new();
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse interview questions JSON");
            return new List<string>
            {
                "Tell me about your most relevant experience for this role.",
                "What technical challenges have you overcome that align with this position?",
            };
        }
    }

    private static string BuildProfileText(ParsedProfile p)
    {
        var sb = new StringBuilder();
        sb.AppendLine(p.Name);
        if (!string.IsNullOrWhiteSpace(p.Summary))
            sb.AppendLine($"Summary: {p.Summary}");
        if (p.Skills.Count > 0)
            sb.AppendLine($"Skills: {string.Join(", ", p.Skills)}");
        foreach (var exp in p.Experience)
            sb.AppendLine($"{exp.Title} at {exp.Company} ({exp.Duration}): {exp.Summary}");
        foreach (var edu in p.EducationList)
            sb.AppendLine($"{edu.Degree} from {edu.Institution} ({edu.Year})");
        return sb.ToString().Trim();
    }

    private static ParsedProfile FallbackProfile(string fileName) =>
        new(FileName:      fileName,
            Name:          Path.GetFileNameWithoutExtension(fileName),
            Email:         string.Empty,
            Phone:         string.Empty,
            Location:      string.Empty,
            Summary:       "Profile could not be fully parsed.",
            Skills:        new(),
            Experience:    new(),
            EducationList: new());

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

public record ResumeInput(string FileName, string Text);

public record WorkExperience(string Title, string Company, string Duration, string Summary);

public record Education(string Degree, string Institution, string Year);

public record ParsedProfile(
    string              FileName,
    string              Name,
    string              Email,
    string              Phone,
    string              Location,
    string              Summary,
    List<string>        Skills,
    List<WorkExperience> Experience,
    List<Education>     EducationList);

public record ScoredCandidate(
    int          Rank,
    float        SimilarityScore,
    ParsedProfile Profile,
    List<string> InterviewQuestions);

public record RankingResult(
    List<ScoredCandidate> RankedCandidates,
    int                   TotalCandidatesEvaluated,
    int                   ShortlistedCount,
    DateTime              EvaluatedAt);

public record StageEvent(string Message, int StageIndex, int TotalStages);

public class RankingInput
{
    public string           JobDescription      { get; set; } = string.Empty;
    public List<ResumeInput> Resumes            { get; set; } = new();
    public int              TopN               { get; set; } = 5;
    public float            SimilarityThreshold { get; set; } = 0f;
}

// ── JSON DTO types ────────────────────────────────────────────────────────────

file class ProfileDto
{
    [JsonPropertyName("name")]       public string?              Name       { get; set; }
    [JsonPropertyName("email")]      public string?              Email      { get; set; }
    [JsonPropertyName("phone")]      public string?              Phone      { get; set; }
    [JsonPropertyName("location")]   public string?              Location   { get; set; }
    [JsonPropertyName("summary")]    public string?              Summary    { get; set; }
    [JsonPropertyName("skills")]     public List<string>?        Skills     { get; set; }
    [JsonPropertyName("experience")] public List<ExperienceDto>? Experience { get; set; }
    [JsonPropertyName("education")]  public List<EducationDto>?  Education  { get; set; }
}

file class ExperienceDto
{
    [JsonPropertyName("title")]    public string? Title    { get; set; }
    [JsonPropertyName("company")]  public string? Company  { get; set; }
    [JsonPropertyName("duration")] public string? Duration { get; set; }
    [JsonPropertyName("summary")]  public string? Summary  { get; set; }
}

file class EducationDto
{
    [JsonPropertyName("degree")]      public string? Degree      { get; set; }
    [JsonPropertyName("institution")] public string? Institution { get; set; }
    [JsonPropertyName("year")]        public string? Year        { get; set; }
}

file class QuestionsWrapper
{
    [JsonPropertyName("questions")] public List<string>? Questions { get; set; }
}
