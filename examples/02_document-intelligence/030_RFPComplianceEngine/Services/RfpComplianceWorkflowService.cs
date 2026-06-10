using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Twf.Flow.Core;
using Twf.Flow.Core.Extensions;
using Twf.Flow.Nodes.Control;
using Twf.Flow.Nodes.Data;

namespace _030_RFPComplianceEngine.Services;

/// <summary>
/// Builds and executes the RFP compliance analysis pipeline.
///
/// Pipeline stages:
///   1. ValidateInput           — FilterNode:  ensure RFP text and capabilities are present
///   2. ExtractRequirements     — AddStep:     LLM parses RFP into structured requirements
///   3. EmbedRequirements       — AddStep:     batch-embed all requirements for vector search
///   4. ForEach Requirement:
///      a. MatchCapabilities    — AddStep:     embed requirement → Qdrant search capabilities
///      b. CheckCompliance      — AddStep:     LLM checks against regulatory frameworks
///      c. CheckPolicy          — AddStep:     embed → Qdrant search policies → LLM alignment
///   5. DetectPolicyConflicts   — AddStep:     LLM cross-checks all policies for conflicts
///   6. DraftResponse           — AddStep:     LLM assembles professional RFP response
///   7. GenerateGapReport       — AddStep:     LLM generates compliance gap report
///   8. AssembleResult          — AddStep:     merge all outputs into final payload
/// </summary>
public class RfpComplianceWorkflowService(
    ILogger<RfpComplianceWorkflowService> logger,
    QdrantVectorStoreService              vectorStore,
    EmbeddingService                      embeddingService,
    LlmService                            llmService,
    ChunkingService                       chunkingService)
{
    public async Task<WorkflowResult> RunAsync(
        RfpAnalysisRequest            request,
        Func<StageEvent, Task>        sendStageAsync,
        Func<RfpComplianceResult, Task> sendCompleteAsync,
        string                        apiKey,
        string                        llmModel,
        string                        llmEndpoint,
        string                        embeddingModel,
        string                        embeddingEndpoint,
        CancellationToken             ct = default)
    {
        var workflow = BuildWorkflow(
            request, sendStageAsync, sendCompleteAsync,
            apiKey, llmModel, llmEndpoint, embeddingModel, embeddingEndpoint, ct);

        var input = WorkflowData
            .From("rfp_text",             request.RfpText)
            .Set("capabilities_text",     request.CapabilitiesText)
            .Set("policies_text",         request.PoliciesText)
            .Set("regulations_text",      request.RegulationsText)
            .Set("frameworks",            string.Join(", ", request.Frameworks))
            .Set("capabilities_collection", request.CapabilitiesCollection)
            .Set("policies_collection",     request.PoliciesCollection)
            .Set("regulations_collection",  request.RegulationsCollection);

        var context = new WorkflowContext("RfpComplianceEngine", logger);
        return await workflow.RunAsync(input, context, ct);
    }

    private Workflow BuildWorkflow(
        RfpAnalysisRequest              request,
        Func<StageEvent, Task>          sendStageAsync,
        Func<RfpComplianceResult, Task> sendCompleteAsync,
        string                          apiKey,
        string                          llmModel,
        string                          llmEndpoint,
        string                          embeddingModel,
        string                          embeddingEndpoint,
        CancellationToken               ct = default)
    {
        var workflow = Workflow.Create("RfpComplianceEngine").UseLogger(logger);

        // ── 1. Validate input ────────────────────────────────────────────────
        workflow.AddNode(
            new FilterNode("ValidateInput")
                .RequireNonEmpty("rfp_text")
                .RequireNonEmpty("capabilities_text")
                .MaxLength("rfp_text", 100_000));

        // ── 2. Extract requirements from RFP (#30) ──────────────────────────
        workflow.AddStep("ExtractRequirements", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Extracting RFP requirements...", 1, 5));

            var rfpText = data.GetString("rfp_text") ?? string.Empty;
            var truncated = rfpText.Length > 30_000 ? rfpText[..30_000] : rfpText;

            var prompt = Constants.Prompts.RequirementsExtractionPrompt
                .Replace("{{rfp_text}}", truncated);

            var json = await llmService.CompleteAsync(
                Constants.Prompts.RequirementsExtractionSystemPrompt,
                prompt, apiKey, llmModel, llmEndpoint, maxTokens: 4000, ct);

            var stripped = StripCodeFences(json);
            var parsed = ParseJson<RequirementsWrapper>(stripped);
            data.Set("requirements", parsed?.Requirements ?? new());
            data.Set("requirement_count", (parsed?.Requirements?.Count ?? 0).ToString());

            logger.LogInformation("Extracted {Count} requirements from RFP", parsed?.Requirements?.Count ?? 0);
            return data;
        });

        // ── 3. Index capabilities into Qdrant (#30) ─────────────────────────
        workflow.AddStep("IndexCapabilities", async (data, _) =>
        {
            var capsText = data.GetString("capabilities_text") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(capsText)) return data;

            var chunks = chunkingService.Chunk(capsText, 400, 50);
            if (chunks.Count == 0) return data;

            const int batchSize = 200;
            const int maxChunkChars = 8_000;
            var texts = chunks
                .Select(c => c.Text.Length > maxChunkChars ? c.Text[..maxChunkChars] : c.Text)
                .ToList();
            var vectors = new List<float[]>(texts.Count);

            for (int i = 0; i < texts.Count; i += batchSize)
            {
                var batch = texts.Skip(i).Take(batchSize);
                var batchVectors = await embeddingService.EmbedBatchAsync(
                    batch, apiKey, embeddingModel, embeddingEndpoint, ct);
                vectors.AddRange(batchVectors);
            }

            var docId = $"capabilities_{Guid.NewGuid():N}";
            var vectorChunks = chunks
                .Zip(vectors, (chunk, vec) => new VectorChunk(
                    Id: $"{docId}_chunk_{chunk.Index}",
                    Text: chunk.Text,
                    Embedding: vec,
                    DocumentId: docId,
                    Title: "Company Capabilities",
                    DocType: "capability",
                    ChunkIndex: chunk.Index))
                .ToList();

            var coll = data.GetString("capabilities_collection") ?? "capabilities";
            await vectorStore.UpsertChunksAsync(vectorChunks, coll, ct);

            logger.LogInformation("Indexed {Count} capability chunks", vectorChunks.Count);
            return data;
        });

        // ── 4. Index policies into Qdrant (#33) ─────────────────────────────
        workflow.AddStep("IndexPolicies", async (data, _) =>
        {
            var polText = data.GetString("policies_text") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(polText)) return data;

            var chunks = chunkingService.Chunk(polText, 400, 50);
            if (chunks.Count == 0) return data;

            const int batchSize = 200;
            const int maxChunkChars = 8_000;
            var texts = chunks
                .Select(c => c.Text.Length > maxChunkChars ? c.Text[..maxChunkChars] : c.Text)
                .ToList();
            var vectors = new List<float[]>(texts.Count);

            for (int i = 0; i < texts.Count; i += batchSize)
            {
                var batch = texts.Skip(i).Take(batchSize);
                var batchVectors = await embeddingService.EmbedBatchAsync(
                    batch, apiKey, embeddingModel, embeddingEndpoint, ct);
                vectors.AddRange(batchVectors);
            }

            var docId = $"policies_{Guid.NewGuid():N}";
            var vectorChunks = chunks
                .Zip(vectors, (chunk, vec) => new VectorChunk(
                    Id: $"{docId}_chunk_{chunk.Index}",
                    Text: chunk.Text,
                    Embedding: vec,
                    DocumentId: docId,
                    Title: "Internal Policies",
                    DocType: "policy",
                    ChunkIndex: chunk.Index))
                .ToList();

            var coll = data.GetString("policies_collection") ?? "policies";
            await vectorStore.UpsertChunksAsync(vectorChunks, coll, ct);

            logger.LogInformation("Indexed {Count} policy chunks", vectorChunks.Count);
            return data;
        });

        // ── 5. Index regulations into Qdrant (#31) ──────────────────────────
        workflow.AddStep("IndexRegulations", async (data, _) =>
        {
            var regText = data.GetString("regulations_text") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(regText)) return data;

            var chunks = chunkingService.Chunk(regText, 400, 50);
            if (chunks.Count == 0) return data;

            const int batchSize = 200;
            const int maxChunkChars = 8_000;
            var texts = chunks
                .Select(c => c.Text.Length > maxChunkChars ? c.Text[..maxChunkChars] : c.Text)
                .ToList();
            var vectors = new List<float[]>(texts.Count);

            for (int i = 0; i < texts.Count; i += batchSize)
            {
                var batch = texts.Skip(i).Take(batchSize);
                var batchVectors = await embeddingService.EmbedBatchAsync(
                    batch, apiKey, embeddingModel, embeddingEndpoint, ct);
                vectors.AddRange(batchVectors);
            }

            var docId = $"regulations_{Guid.NewGuid():N}";
            var vectorChunks = chunks
                .Zip(vectors, (chunk, vec) => new VectorChunk(
                    Id: $"{docId}_chunk_{chunk.Index}",
                    Text: chunk.Text,
                    Embedding: vec,
                    DocumentId: docId,
                    Title: "Regulatory Frameworks",
                    DocType: "regulation",
                    ChunkIndex: chunk.Index))
                .ToList();

            var coll = data.GetString("regulations_collection") ?? "regulations";
            await vectorStore.UpsertChunksAsync(vectorChunks, coll, ct);

            logger.LogInformation("Indexed {Count} regulation chunks", vectorChunks.Count);
            return data;
        });

        // ── 6. Process each requirement (#30 + #31 + #33) ───────────────────
        workflow.AddStep("ProcessRequirements", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Analyzing requirements...", 2, 5));

            var requirements = data.Get<List<RfpRequirement>>("requirements") ?? new();
            var capsColl  = data.GetString("capabilities_collection") ?? "capabilities";
            var polColl   = data.GetString("policies_collection")     ?? "policies";
            var regColl   = data.GetString("regulations_collection")  ?? "regulations";
            var frameworks = data.GetString("frameworks") ?? "GDPR, HIPAA, SOC2";

            var complianceResults  = new List<ComplianceCheckResult>();
            var policyResults      = new List<PolicyCheckResult>();
            var capabilityMatches  = new List<CapabilityMatchResult>();

            foreach (var req in requirements)
            {
                // 6a. Match capabilities (#30)
                var capVector = await embeddingService.EmbedAsync(
                    req.Description, apiKey, embeddingModel, embeddingEndpoint, ct);
                var capChunks = await vectorStore.SearchAsync(capVector, 5, capsColl, ct);
                var capsContext = BuildChunkContext(capChunks);

                var capPrompt = Constants.Prompts.CapabilityMatchingPrompt
                    .Replace("{{requirement}}", $"ID: {req.Id}\nDescription: {req.Description}\nCategory: {req.Category}\nPriority: {req.Priority}")
                    .Replace("{{capabilities_context}}", capsContext);

                var capJson = await llmService.CompleteAsync(
                    Constants.Prompts.CapabilityMatchingSystemPrompt,
                    capPrompt, apiKey, llmModel, llmEndpoint, maxTokens: 2000, ct);

                var capParsed = ParseJson<CapabilityMatchResult>(StripCodeFences(capJson));
                if (capParsed != null)
                {
                    capParsed.RequirementId = req.Id;
                    capabilityMatches.Add(capParsed);
                }

                // 6b. Check compliance (#31)
                var regVector = await embeddingService.EmbedAsync(
                    req.Description, apiKey, embeddingModel, embeddingEndpoint, ct);
                var regChunks = await vectorStore.SearchAsync(regVector, 8, regColl, ct);
                var regsContext = BuildChunkContext(regChunks);

                var responseText = capParsed?.Response ?? "No response drafted yet.";

                var compPrompt = Constants.Prompts.ComplianceCheckPrompt
                    .Replace("{{requirement}}", $"ID: {req.Id}\nDescription: {req.Description}")
                    .Replace("{{response}}", responseText)
                    .Replace("{{frameworks}}", frameworks)
                    .Replace("{{regulations_context}}", regsContext);

                var compSystemPrompt = Constants.Prompts.ComplianceCheckSystemPrompt
                    .Replace("{{frameworks}}", frameworks);

                var compJson = await llmService.CompleteAsync(
                    compSystemPrompt, compPrompt, apiKey, llmModel, llmEndpoint, maxTokens: 3000, ct);

                var compParsed = ParseJson<ComplianceCheckResult>(StripCodeFences(compJson));
                if (compParsed != null)
                {
                    compParsed.RequirementId = req.Id;
                    complianceResults.Add(compParsed);
                }

                // 6c. Check policy alignment (#33)
                var polVector = await embeddingService.EmbedAsync(
                    $"{req.Description} {responseText}", apiKey, embeddingModel, embeddingEndpoint, ct);
                var polChunks = await vectorStore.SearchAsync(polVector, 5, polColl, ct);
                var polsContext = BuildChunkContext(polChunks);

                var polPrompt = Constants.Prompts.PolicyAlignmentPrompt
                    .Replace("{{requirement}}", $"ID: {req.Id}\nDescription: {req.Description}")
                    .Replace("{{response}}", responseText)
                    .Replace("{{policies_context}}", polsContext);

                var polJson = await llmService.CompleteAsync(
                    Constants.Prompts.PolicyAlignmentSystemPrompt,
                    polPrompt, apiKey, llmModel, llmEndpoint, maxTokens: 2000, ct);

                var polParsed = ParseJson<PolicyCheckResult>(StripCodeFences(polJson));
                if (polParsed != null)
                {
                    polParsed.RequirementId = req.Id;
                    policyResults.Add(polParsed);
                }

                logger.LogInformation("Processed requirement {ReqId}", req.Id);
            }

            data.Set("compliance_results", complianceResults);
            data.Set("policy_results", policyResults);
            data.Set("capability_matches", capabilityMatches);
            return data;
        });

        // ── 7. Detect policy conflicts (#33) ────────────────────────────────
        workflow.AddStep("DetectPolicyConflicts", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Detecting policy conflicts...", 3, 5));

            var policyResults = data.Get<List<PolicyCheckResult>>("policy_results") ?? new();
            var summary = JsonSerializer.Serialize(policyResults, JsonOpts);

            var prompt = Constants.Prompts.PolicyConflictPrompt
                .Replace("{{policy_alignment_summary}}", summary);

            var json = await llmService.CompleteAsync(
                Constants.Prompts.PolicyConflictSystemPrompt,
                prompt, apiKey, llmModel, llmEndpoint, maxTokens: 2000, ct);

            var parsed = ParseJson<PolicyConflictReport>(StripCodeFences(json));
            data.Set("policy_conflicts", parsed ?? new PolicyConflictReport());

            return data;
        });

        // ── 8. Draft RFP response (#30) ─────────────────────────────────────
        workflow.AddStep("DraftResponse", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Drafting RFP response...", 4, 5));

            var requirements      = data.Get<List<RfpRequirement>>("requirements")          ?? new();
            var capabilityMatches = data.Get<List<CapabilityMatchResult>>("capability_matches") ?? new();
            var complianceResults = data.Get<List<ComplianceCheckResult>>("compliance_results") ?? new();
            var policyResults     = data.Get<List<PolicyCheckResult>>("policy_results")         ?? new();

            var combined = new
            {
                Requirements = requirements,
                CapabilityMatches = capabilityMatches,
                ComplianceResults = complianceResults,
                PolicyResults = policyResults,
            };

            var prompt = Constants.Prompts.ResponseDraftingPrompt
                .Replace("{{requirement_results}}", JsonSerializer.Serialize(combined, JsonOpts));

            var json = await llmService.CompleteAsync(
                Constants.Prompts.ResponseDraftingSystemPrompt,
                prompt, apiKey, llmModel, llmEndpoint, maxTokens: 6000, ct);

            var parsed = ParseJson<DraftedResponse>(StripCodeFences(json));
            data.Set("drafted_response", parsed);

            return data;
        });

        // ── 9. Generate gap report (#31) ────────────────────────────────────
        workflow.AddStep("GenerateGapReport", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Generating compliance gap report...", 5, 5));

            var complianceResults = data.Get<List<ComplianceCheckResult>>("compliance_results") ?? new();
            var policyResults     = data.Get<List<PolicyCheckResult>>("policy_results")         ?? new();

            var prompt = Constants.Prompts.GapReportPrompt
                .Replace("{{compliance_findings}}", JsonSerializer.Serialize(complianceResults, JsonOpts))
                .Replace("{{policy_alignment_summary}}", JsonSerializer.Serialize(policyResults, JsonOpts));

            var json = await llmService.CompleteAsync(
                Constants.Prompts.GapReportSystemPrompt,
                prompt, apiKey, llmModel, llmEndpoint, maxTokens: 4000, ct);

            var parsed = ParseJson<ComplianceGapReport>(StripCodeFences(json));
            data.Set("gap_report", parsed ?? new ComplianceGapReport());

            return data;
        });

        // ── 10. Assemble final result ───────────────────────────────────────
        workflow.AddStep("AssembleResult", async (data, _) =>
        {
            var requirements       = data.Get<List<RfpRequirement>>("requirements")              ?? new();
            var complianceResults  = data.Get<List<ComplianceCheckResult>>("compliance_results")  ?? new();
            var policyResults      = data.Get<List<PolicyCheckResult>>("policy_results")          ?? new();
            var policyConflicts    = data.Get<PolicyConflictReport>("policy_conflicts")           ?? new();
            var draftedResponse    = data.Get<DraftedResponse>("drafted_response");
            var gapReport          = data.Get<ComplianceGapReport>("gap_report")                  ?? new();

            var responseDocument = draftedResponse?.ResponseDocument ?? string.Empty;

            var result = new RfpComplianceResult(
                Requirements:       requirements,
                ComplianceResults:  complianceResults,
                PolicyResults:      policyResults,
                PolicyConflicts:    policyConflicts,
                DraftedResponse:    responseDocument,
                GapReport:          gapReport,
                AnalyzedAt:         DateTime.UtcNow);

            await sendCompleteAsync(result);
            return data;
        });

        return workflow;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildChunkContext(List<VectorChunk> chunks)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < chunks.Count; i++)
        {
            sb.AppendLine($"[doc_{i + 1}] {chunks[i].Title} | Type: {chunks[i].DocType}");
            sb.AppendLine(chunks[i].Text);
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

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

    private T? ParseJson<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse JSON for type {Type}", typeof(T).Name);
            return default;
        }
    }
}

// ── Domain types ──────────────────────────────────────────────────────────────

public record StageEvent(string Message, int StageIndex, int TotalStages);

public class RfpRequirement
{
    [JsonPropertyName("id")]                   public string       Id                   { get; set; } = string.Empty;
    [JsonPropertyName("description")]          public string       Description          { get; set; } = string.Empty;
    [JsonPropertyName("category")]             public string       Category             { get; set; } = string.Empty;
    [JsonPropertyName("priority")]             public string       Priority             { get; set; } = string.Empty;
    [JsonPropertyName("compliance_frameworks")] public List<string> ComplianceFrameworks { get; set; } = new();
}

public class CapabilityMatchResult
{
    [JsonPropertyName("requirement_id")]    public string       RequirementId    { get; set; } = string.Empty;
    [JsonPropertyName("response")]          public string       Response         { get; set; } = string.Empty;
    [JsonPropertyName("capability_matches")] public List<string> CapabilityMatches { get; set; } = new();
    [JsonPropertyName("confidence")]        public string       Confidence       { get; set; } = string.Empty;
    [JsonPropertyName("gaps")]              public string       Gaps             { get; set; } = string.Empty;
}

public class ComplianceCheckResult
{
    [JsonPropertyName("requirement_id")]      public string                  RequirementId     { get; set; } = string.Empty;
    [JsonPropertyName("overall_compliance")]  public string                  OverallCompliance { get; set; } = string.Empty;
    [JsonPropertyName("findings")]            public List<ComplianceFinding> Findings          { get; set; } = new();
    [JsonPropertyName("summary")]             public string                  Summary           { get; set; } = string.Empty;
}

public class ComplianceFinding
{
    [JsonPropertyName("framework")]      public string Framework      { get; set; } = string.Empty;
    [JsonPropertyName("clause")]         public string Clause         { get; set; } = string.Empty;
    [JsonPropertyName("status")]         public string Status         { get; set; } = string.Empty;
    [JsonPropertyName("issue")]          public string Issue          { get; set; } = string.Empty;
    [JsonPropertyName("risk_level")]     public string RiskLevel      { get; set; } = string.Empty;
    [JsonPropertyName("recommendation")] public string Recommendation { get; set; } = string.Empty;
}

public class PolicyCheckResult
{
    [JsonPropertyName("requirement_id")]  public string                RequirementId { get; set; } = string.Empty;
    [JsonPropertyName("alignment")]       public string                Alignment     { get; set; } = string.Empty;
    [JsonPropertyName("citations")]       public List<PolicyCitation>  Citations     { get; set; } = new();
    [JsonPropertyName("conflicts")]       public List<string>          Conflicts     { get; set; } = new();
    [JsonPropertyName("recommendations")] public string                Recommendations { get; set; } = string.Empty;
}

public class PolicyCitation
{
    [JsonPropertyName("policy_id")]    public string PolicyId    { get; set; } = string.Empty;
    [JsonPropertyName("policy_title")] public string PolicyTitle { get; set; } = string.Empty;
    [JsonPropertyName("section")]      public string Section     { get; set; } = string.Empty;
    [JsonPropertyName("excerpt")]      public string Excerpt     { get; set; } = string.Empty;
    [JsonPropertyName("alignment")]    public string Alignment   { get; set; } = string.Empty;
}

public class PolicyConflictReport
{
    [JsonPropertyName("conflicts")]          public List<PolicyConflict>      Conflicts        { get; set; } = new();
    [JsonPropertyName("outdated_policies")]  public List<OutdatedPolicy>      OutdatedPolicies { get; set; } = new();
    [JsonPropertyName("coverage_gaps")]      public List<string>              CoverageGaps     { get; set; } = new();
}

public class PolicyConflict
{
    [JsonPropertyName("policy_a")]     public string PolicyA     { get; set; } = string.Empty;
    [JsonPropertyName("policy_b")]     public string PolicyB     { get; set; } = string.Empty;
    [JsonPropertyName("description")]  public string Description { get; set; } = string.Empty;
    [JsonPropertyName("severity")]     public string Severity    { get; set; } = string.Empty;
}

public class OutdatedPolicy
{
    [JsonPropertyName("policy_id")]       public string PolicyId       { get; set; } = string.Empty;
    [JsonPropertyName("policy_title")]    public string PolicyTitle    { get; set; } = string.Empty;
    [JsonPropertyName("last_reviewed")]   public string LastReviewed   { get; set; } = string.Empty;
    [JsonPropertyName("recommendation")]  public string Recommendation { get; set; } = string.Empty;
}

public class DraftedResponse
{
    [JsonPropertyName("response_document")]    public string ResponseDocument    { get; set; } = string.Empty;
    [JsonPropertyName("executive_summary")]    public string ExecutiveSummary    { get; set; } = string.Empty;
    [JsonPropertyName("total_requirements")]   public int    TotalRequirements   { get; set; }
    [JsonPropertyName("fully_addressed")]      public int    FullyAddressed      { get; set; }
    [JsonPropertyName("partially_addressed")]  public int    PartiallyAddressed  { get; set; }
    [JsonPropertyName("not_addressed")]        public int    NotAddressed        { get; set; }
}

public class ComplianceGapReport
{
    [JsonPropertyName("executive_summary")]   public string              ExecutiveSummary { get; set; } = string.Empty;
    [JsonPropertyName("overall_risk")]        public string              OverallRisk      { get; set; } = string.Empty;
    [JsonPropertyName("total_requirements")]  public int                 TotalRequirements { get; set; }
    [JsonPropertyName("compliant")]           public int                 Compliant        { get; set; }
    [JsonPropertyName("partially_compliant")] public int                 PartiallyCompliant { get; set; }
    [JsonPropertyName("non_compliant")]       public int                 NonCompliant     { get; set; }
    [JsonPropertyName("critical_gaps")]       public List<CriticalGap>   CriticalGaps     { get; set; } = new();
    [JsonPropertyName("remediation_roadmap")] public string              RemediationRoadmap { get; set; } = string.Empty;
}

public class CriticalGap
{
    [JsonPropertyName("requirement_id")]  public string RequirementId  { get; set; } = string.Empty;
    [JsonPropertyName("framework")]       public string Framework      { get; set; } = string.Empty;
    [JsonPropertyName("gap")]             public string Gap            { get; set; } = string.Empty;
    [JsonPropertyName("risk_level")]      public string RiskLevel      { get; set; } = string.Empty;
    [JsonPropertyName("remediation")]     public string Remediation    { get; set; } = string.Empty;
    [JsonPropertyName("estimated_effort")] public string EstimatedEffort { get; set; } = string.Empty;
}

public record RfpComplianceResult(
    List<RfpRequirement>        Requirements,
    List<ComplianceCheckResult> ComplianceResults,
    List<PolicyCheckResult>     PolicyResults,
    PolicyConflictReport        PolicyConflicts,
    string                      DraftedResponse,
    ComplianceGapReport         GapReport,
    DateTime                    AnalyzedAt);

public class RfpAnalysisRequest
{
    public string       RfpText                 { get; set; } = string.Empty;
    public string       CapabilitiesText        { get; set; } = string.Empty;
    public string       PoliciesText            { get; set; } = string.Empty;
    public string       RegulationsText         { get; set; } = string.Empty;
    public List<string> Frameworks              { get; set; } = new() { "GDPR", "SOC2" };
    public string       CapabilitiesCollection  { get; set; } = "capabilities";
    public string       PoliciesCollection      { get; set; } = "policies";
    public string       RegulationsCollection   { get; set; } = "regulations";
    public int          ChunkSize               { get; set; } = 400;
    public int          ChunkOverlap            { get; set; } = 50;
}

// ── JSON wrapper types ────────────────────────────────────────────────────────

file class RequirementsWrapper
{
    [JsonPropertyName("requirements")] public List<RfpRequirement>? Requirements { get; set; }
}
