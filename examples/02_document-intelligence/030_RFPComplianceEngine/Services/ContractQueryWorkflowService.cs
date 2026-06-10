using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Twf.Flow.Core;
using Twf.Flow.Core.Extensions;
using Twf.Flow.Nodes.Control;

namespace _030_RFPComplianceEngine.Services;

/// <summary>
/// Builds and executes the contract query pipeline.
///
/// Pipeline stages:
///   1. ValidateInput     — FilterNode:  ensure question is non-empty
///   2. EmbedQuery        — AddStep:     embed the user's question
///   3. SearchContracts   — AddStep:     Qdrant semantic search over contracts collection
///   4. BuildContext      — AddStep:     format retrieved chunks as numbered context blocks
///   5. SynthesizeAnswer  — AddStep:     LLM answers using only retrieved context
///   6. ExtractCitations  — AddStep:     parse structured citations from LLM output
///   7. AssembleResult    — AddStep:     build final payload and fire SSE complete event
/// </summary>
public class ContractQueryWorkflowService(
    ILogger<ContractQueryWorkflowService> logger,
    QdrantVectorStoreService              vectorStore,
    EmbeddingService                      embeddingService,
    LlmService                            llmService)
{
    public async Task<WorkflowResult> RunAsync(
        ContractQuery                    query,
        Func<StageEvent, Task>           sendStageAsync,
        Func<ContractQueryResult, Task>  sendCompleteAsync,
        string                           apiKey,
        string                           llmModel,
        string                           llmEndpoint,
        string                           embeddingModel,
        string                           embeddingEndpoint,
        CancellationToken                ct = default)
    {
        var workflow = BuildWorkflow(
            query, sendStageAsync, sendCompleteAsync,
            apiKey, llmModel, llmEndpoint, embeddingModel, embeddingEndpoint, ct);

        var input = WorkflowData
            .From("question",  query.Question)
            .Set("top_k",      query.TopK.ToString())
            .Set("collection", query.Collection);

        var context = new WorkflowContext("ContractQuery", logger);
        return await workflow.RunAsync(input, context, ct);
    }

    private Workflow BuildWorkflow(
        ContractQuery                    query,
        Func<StageEvent, Task>           sendStageAsync,
        Func<ContractQueryResult, Task>  sendCompleteAsync,
        string                           apiKey,
        string                           llmModel,
        string                           llmEndpoint,
        string                           embeddingModel,
        string                           embeddingEndpoint,
        CancellationToken                ct)
    {
        var workflow = Workflow.Create("ContractQuery").UseLogger(logger);

        // ── 1. Validate input ────────────────────────────────────────────────
        workflow.AddStep("ValidateInput", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Validating input...", 0, 4));

            var question = data.GetString("question") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(question))
                throw new InvalidOperationException("Question cannot be empty.");
            if (question.Length > 2_000)
                throw new InvalidOperationException("Question exceeds maximum length of 2,000 characters.");

            return data;
        });

        // ── 2. Embed query ───────────────────────────────────────────────────
        workflow.AddStep("EmbedQuery", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Embedding question...", 1, 4));

            var question = data.GetString("question") ?? string.Empty;
            var vector   = await embeddingService.EmbedAsync(
                question, apiKey, embeddingModel, embeddingEndpoint, ct);

            data.Set("query_vector", vector);
            return data;
        });

        // ── 3. Search contracts ──────────────────────────────────────────────
        workflow.AddStep("SearchContracts", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Searching indexed contracts...", 2, 4));

            var vector     = data.Get<float[]>("query_vector") ?? Array.Empty<float>();
            int topK       = int.TryParse(data.GetString("top_k"), out var k) ? k : 8;
            var collection = data.GetString("collection") ?? "contracts";

            var chunks = await vectorStore.SearchAsync(vector, topK, collection, ct);
            data.Set("retrieved_chunks", chunks);
            data.Set("retrieved_chunk_count", chunks.Count.ToString());

            logger.LogInformation("Retrieved {Count} contract chunks for query", chunks.Count);
            return data;
        });

        // ── 4. Build context ─────────────────────────────────────────────────
        workflow.AddStep("BuildContext", async (data, _) =>
        {
            var chunks = data.Get<List<VectorChunk>>("retrieved_chunks") ?? new();
            var sb     = new StringBuilder();

            for (int i = 0; i < chunks.Count; i++)
            {
                var c = chunks[i];
                sb.AppendLine($"[source_{i + 1}] Document: {c.Title} | Type: {c.DocType} | Chunk: {c.ChunkIndex}");
                sb.AppendLine(c.Text);
                sb.AppendLine();
            }

            data.Set("retrieved_context", sb.ToString().TrimEnd());
            return data;
        });

        // ── 5. Synthesize answer ─────────────────────────────────────────────
        workflow.AddStep("NotifySynthesis", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Synthesizing answer from contracts...", 3, 4));
            return data;
        });

        workflow.AddStep("SynthesizeAnswer", async (data, _) =>
        {
            var context = data.GetString("retrieved_context") ?? string.Empty;
            var question = data.GetString("question") ?? string.Empty;

            var prompt = Constants.Prompts.ContractSynthesisPrompt
                .Replace("{{retrieved_context}}", context)
                .Replace("{{question}}", question);

            var json = await llmService.CompleteAsync(
                Constants.Prompts.ContractSynthesisSystemPrompt,
                prompt, apiKey, llmModel, llmEndpoint, maxTokens: 4000, ct);

            data.Set("llm_raw_output", StripCodeFences(json));
            return data;
        });

        // ── 6. Extract citations ─────────────────────────────────────────────
        workflow.AddStep("ExtractCitations", async (data, _) =>
        {
            var raw = data.GetString("llm_raw_output") ?? "{}";

            string answer;
            List<ContractCitation> citations;

            try
            {
                var parsed = JsonSerializer.Deserialize<ContractSynthesisOutput>(raw, JsonOpts)
                             ?? new ContractSynthesisOutput();
                answer   = parsed.Answer ?? string.Empty;
                citations = parsed.Citations ?? new();
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Failed to parse synthesis JSON — using raw output as answer");
                answer    = raw;
                citations = new();
            }

            data.Set("answer", answer);
            data.Set("citations_json", JsonSerializer.Serialize(citations, JsonOpts));
            return data;
        });

        // ── 7. Assemble result ───────────────────────────────────────────────
        workflow.AddStep("AssembleResult", async (data, _) =>
        {
            var answerText   = data.GetString("answer") ?? string.Empty;
            var citationsRaw = data.GetString("citations_json") ?? "[]";
            var chunkCount   = int.TryParse(data.GetString("retrieved_chunk_count"), out var cc) ? cc : 0;

            List<ContractCitation> citations;
            try
            {
                citations = JsonSerializer.Deserialize<List<ContractCitation>>(citationsRaw, JsonOpts) ?? new();
            }
            catch
            {
                citations = new();
            }

            var result = new ContractQueryResult(
                Answer:              answerText,
                Citations:           citations,
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

// ── Domain types ──────────────────────────────────────────────────────────────

public class ContractQuery
{
    public string Question   { get; set; } = string.Empty;
    public int    TopK       { get; set; } = 8;
    public string Collection { get; set; } = "contracts";
}

public record ContractQueryResult(
    string                   Answer,
    List<ContractCitation>   Citations,
    int                      RetrievedChunkCount,
    DateTime                 QueriedAt);

public class ContractCitation
{
    [JsonPropertyName("source_id")]  public string SourceId  { get; set; } = string.Empty;
    [JsonPropertyName("document_id")] public string DocumentId { get; set; } = string.Empty;
    [JsonPropertyName("title")]      public string Title     { get; set; } = string.Empty;
    [JsonPropertyName("excerpt")]    public string Excerpt   { get; set; } = string.Empty;
}

file class ContractSynthesisOutput
{
    [JsonPropertyName("answer")]    public string?                Answer    { get; set; }
    [JsonPropertyName("citations")] public List<ContractCitation>? Citations { get; set; }
}
