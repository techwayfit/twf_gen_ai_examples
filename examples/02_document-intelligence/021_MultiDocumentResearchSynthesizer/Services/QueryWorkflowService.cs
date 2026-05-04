using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwfAiFramework.Core;
using TwfAiFramework.Core.Extensions;
using TwfAiFramework.Core.ValueObjects;
using TwfAiFramework.Nodes.AI;
using TwfAiFramework.Nodes.Control;
using TwfAiFramework.Nodes.Data;

namespace _021_MultiDocumentResearchSynthesizer.Services;

/// <summary>
/// Builds and executes the multi-document research synthesis pipeline.
///
/// Pipeline stages:
///   1. ValidateInput         — FilterNode:  ensure question is non-empty
///   2. EmbedQuery            — AddStep:     call OpenAI embeddings API
///   3. RetrieveChunks        — AddStep:     semantic search in QdrantVectorStoreService
///   4. BuildGroundedPrompt   — AddStep:     format retrieved chunks as numbered context blocks
///   5. SynthesizeAnswer      — AIPipeline:  LLM generates answer with inline citations
///   6. ExtractCitations      — OutputParserNode: parse answer + citations[]
///   7. DetectContradictions  — AIPipeline:  second LLM pass identifies agreements/contradictions
///   8. ParseAndAssemble      — AddStep:     build final payload and fire SSE complete event
/// </summary>
public class QueryWorkflowService(
    ILogger<QueryWorkflowService>  logger,
    QdrantVectorStoreService       vectorStore,
    EmbeddingService               embeddingService)
{
    // ── Public entry point ────────────────────────────────────────────────────

    public async Task<WorkflowResult> RunAsync(
        ResearchQuery               query,
        Func<StageEvent, Task>      sendStageAsync,
        Func<SynthesisResult, Task> sendCompleteAsync,
        LlmConfig                   llmConfig,
        string                      apiKey,
        string                      embeddingModel,
        string                      embeddingEndpoint,
        CancellationToken           ct = default)
    {
        var workflow = BuildWorkflow(
            query,
            sendStageAsync,
            sendCompleteAsync,
            llmConfig,
            apiKey,
            embeddingModel,
            embeddingEndpoint);

        var input = WorkflowData
            .From("question", query.Question)
            .Set("top_k",     query.TopK.ToString())
            .Set("collection", query.Collection);

        var context = new WorkflowContext("ResearchSynthesizer", logger);
        return await workflow.RunAsync(input, context, ct);
    }

    // ── Workflow builder ──────────────────────────────────────────────────────

    private Workflow BuildWorkflow(
        ResearchQuery               query,
        Func<StageEvent, Task>      sendStageAsync,
        Func<SynthesisResult, Task> sendCompleteAsync,
        LlmConfig                   llmConfig,
        string                      apiKey,
        string                      embeddingModel,
        string                      embeddingEndpoint)
    {
        var workflow = Workflow.Create("ResearchSynthesizer").UseLogger(logger);

        // ── 1. Validate input ────────────────────────────────────────────────
        workflow.AddNode(
            new FilterNode("ValidateInput")
                .RequireNonEmpty("question")
                .MaxLength("question", 2_000));

        // ── 2. Embed query ───────────────────────────────────────────────────
        workflow.AddStep("EmbedQuery", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Embedding research question...", 1, 3));

            var question = data.GetString("question") ?? string.Empty;
            var vector   = await embeddingService.EmbedAsync(
                question, apiKey, embeddingModel, embeddingEndpoint);

            data.Set("query_vector", vector);
            return data;
        });

        // ── 3. Retrieve relevant chunks ──────────────────────────────────────
        workflow.AddStep("RetrieveChunks", async (data, _) =>
        {
            var vector     = data.Get<float[]>("query_vector") ?? Array.Empty<float>();
            int topK       = int.TryParse(data.GetString("top_k"), out var k) ? k : 8;
            var collection = data.GetString("collection");

            var chunks     = await vectorStore.SearchAsync(vector, topK, collection);
            data.Set("retrieved_chunks", chunks);
            data.Set("retrieved_chunk_count", chunks.Count.ToString());

            logger.LogInformation("Retrieved {Count} chunks for synthesis", chunks.Count);
            return data;
        });

        // ── 4. Build grounded prompt context ─────────────────────────────────
        workflow.AddStep("BuildContext", async (data, _) =>
        {
            var chunks = data.Get<List<VectorChunk>>("retrieved_chunks") ?? new();
            var sb     = new StringBuilder();

            for (int i = 0; i < chunks.Count; i++)
            {
                var c = chunks[i];
                sb.AppendLine($"[source_{i + 1}] Title: {c.Title} | Authors: {c.Authors} | Year: {c.Year} | Page: {c.Page}");
                sb.AppendLine(c.Text);
                sb.AppendLine();
            }

            data.Set("retrieved_context", sb.ToString().TrimEnd());
            return data;
        });

        // ── 5. Synthesize answer with citations ──────────────────────────────
        workflow.AddStep("NotifySynthesisStage", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Synthesizing answer from retrieved sources...", 2, 3));
            return data;
        });

        workflow.AddAIPipeline(new AIPipelineConfig
        {
            NodePrefix     = "Synthesis",
            Llm            = llmConfig with { MaxTokens = TokenCount.Standard },
            PromptTemplate = Constants.Prompts.SynthesisPrompt,
            SystemTemplate = Constants.Prompts.SynthesisSystemPrompt,
        });

        // ── 6. Extract structured citations ──────────────────────────────────
        workflow.AddNode(OutputParserNode.WithMapping("ExtractCitations",
            ("answer",    "answer"),
            ("citations", "citations")));

        // ── 6b. Re-serialize citations JsonElement → plain string ─────────────
        // OutputParserNode stores array fields as JsonElement; GetString() on a
        // JsonElement array throws — so we call GetRawText() and store the result
        // as a guaranteed string under "citations_json".
        workflow.AddStep("SerializeCitations", async (data, _) =>
        {
            string citationsJson;
            try
            {
                var el = data.Get<System.Text.Json.JsonElement>("citations");
                citationsJson = el.GetRawText();
            }
            catch
            {
                citationsJson = "[]";
            }
            data.Set("citations_json", citationsJson);
            return data;
        });

        // ── 7. Detect contradictions ─────────────────────────────────────────
        workflow.AddStep("NotifyContradictionStage", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Detecting agreements and contradictions...", 3, 3));

            // citations_json is a guaranteed plain string set by SerializeCitations
            data.Set("citations_text", data.GetString("citations_json") ?? "[]");
            return data;
        });

        workflow.AddAIPipeline(new AIPipelineConfig
        {
            NodePrefix     = "Contradictions",
            Llm            = llmConfig with { MaxTokens = TokenCount.FromValue(1_200) },
            PromptTemplate = Constants.Prompts.ContradictionPrompt,
            SystemTemplate = Constants.Prompts.ContradictionSystemPrompt,
        });

        // ── 8. Parse and assemble final result ───────────────────────────────
        workflow.AddStep("ParseAndAssemble", async (data, _) =>
        {
            var rawContra = data.LlmResponse();
            var answerText   = data.GetString("answer") ?? string.Empty;
            var citationsRaw = data.GetString("citations_json") ?? "[]";
            rawContra        = StripCodeFences(rawContra ?? string.Empty);

            List<Citation> citations;
            try
            {
                var parsed = JsonSerializer.Deserialize<List<CitationDto>>(citationsRaw, JsonOpts)
                             ?? new List<CitationDto>();
                citations = parsed.Select(c => c.ToRecord()).ToList();
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Failed to parse citations JSON — continuing with empty citations");
                citations = new List<Citation>();
            }

            List<Contradiction> contradictions;
            try
            {
                var contraDto = JsonSerializer.Deserialize<ContradictionWrapper>(rawContra, JsonOpts)
                                ?? new ContradictionWrapper();
                contradictions = (contraDto.Contradictions ?? new()).Select(c => c.ToRecord()).ToList();
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Failed to parse contradiction JSON — continuing without contradictions");
                contradictions = new List<Contradiction>();
            }

            var chunkCount = int.TryParse(data.GetString("retrieved_chunk_count"), out var cc) ? cc : 0;

            var result = new SynthesisResult(
                Answer:              answerText,
                Citations:           citations,
                Contradictions:      contradictions,
                RetrievedChunkCount: chunkCount,
                QueriedAt:           DateTime.UtcNow);

            await sendCompleteAsync(result);
            return data;
        });

        return workflow;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
    };

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

public record Citation(
    string       SourceId,
    string       PaperId,
    string       Title,
    List<string> Authors,
    int          Year,
    int          Page,
    string       Excerpt);

public record Contradiction(
    string Claim,
    string SourceA,
    string SourceB,
    string Summary);

public record SynthesisResult(
    string             Answer,
    List<Citation>     Citations,
    List<Contradiction> Contradictions,
    int                RetrievedChunkCount,
    DateTime           QueriedAt);

public class ResearchQuery
{
    public string Question   { get; set; } = string.Empty;
    public int    TopK       { get; set; } = 8;
    public string Collection { get; set; } = string.Empty;
}

// ── JSON DTO types ────────────────────────────────────────────────────────────

file class CitationDto
{
    [JsonPropertyName("source_id")]  public string       SourceId { get; set; } = string.Empty;
    [JsonPropertyName("paper_id")]   public string       PaperId  { get; set; } = string.Empty;
    [JsonPropertyName("title")]      public string       Title    { get; set; } = string.Empty;
    [JsonPropertyName("authors")]    public List<string> Authors  { get; set; } = new();
    [JsonPropertyName("year")]       public int          Year     { get; set; }
    [JsonPropertyName("page")]       public int          Page     { get; set; }
    [JsonPropertyName("excerpt")]    public string       Excerpt  { get; set; } = string.Empty;

    public Citation ToRecord() => new(SourceId, PaperId, Title, Authors, Year, Page, Excerpt);
}

file class ContradictionWrapper
{
    [JsonPropertyName("contradictions")]
    public List<ContradictionDto>? Contradictions { get; set; }
}

file class ContradictionDto
{
    [JsonPropertyName("claim")]    public string Claim   { get; set; } = string.Empty;
    [JsonPropertyName("source_a")] public string SourceA { get; set; } = string.Empty;
    [JsonPropertyName("source_b")] public string SourceB { get; set; } = string.Empty;
    [JsonPropertyName("summary")]  public string Summary { get; set; } = string.Empty;

    public Contradiction ToRecord() => new(Claim, SourceA, SourceB, Summary);
}
