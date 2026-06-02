using System.Text.Json;
using _036_AI_Pair_Programmer.Models;
using Markdig;
using TwfAiFramework.Core;
using TwfAiFramework.Core.Extensions;
using TwfAiFramework.Nodes.Data;

namespace _036_AI_Pair_Programmer.Services;

public sealed class PairProgrammingWorkflowService(
    ILogger<PairProgrammingWorkflowService> logger,
    CodeIndexStoreService indexStore,
    QdrantVectorStoreService qdrantStore,
    IEmbeddingService embeddingService,
    LlmService llmService)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseAdvancedExtensions()
        .Build();

    public async Task<PairProgrammerResult> RunAsync(
        QueryRequest request,
        string apiKey,
        string chatModel,
        string endpoint,
        CancellationToken ct = default)
    {
        var workflow = BuildWorkflow(apiKey, chatModel, endpoint, ct);

        var input = WorkflowData
            .From("repo_path", request.RepoPath)
            .Set("user_request", request.UserRequest)
            .Set("top_k", Math.Clamp(request.TopK, 1, 20).ToString())
            .Set("task_type", string.IsNullOrWhiteSpace(request.TaskType) ? "implement" : request.TaskType);

        var context = new WorkflowContext("PairProgramming", logger);
        var result = await workflow.RunAsync(input, context, ct);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.ErrorMessage ?? "Pair programming workflow failed.");
        }

        return result.Data.Get<PairProgrammerResult>("result") ?? new PairProgrammerResult
        {
            Summary = "No response was generated.",
            Risks = ["The model response did not include the expected output shape."]
        };
    }

    private Workflow BuildWorkflow(
        string apiKey,
        string chatModel,
        string endpoint,
        CancellationToken ct)
    {
        var workflow = Workflow.Create("AiPairProgrammer").UseLogger(logger);

        workflow.AddNode(new FilterNode("ValidateQueryInput")
            .RequireNonEmpty("repo_path")
            .RequireNonEmpty("user_request"));

        workflow.AddStep("RetrieveContext", async (data, _) =>
        {
            var repoPath = data.GetString("repo_path") ?? string.Empty;
            var topK = int.TryParse(data.GetString("top_k"), out var parsedTopK) ? parsedTopK : 8;
            var userRequest = data.GetString("user_request") ?? string.Empty;
            var queryEmbedding = await embeddingService.EmbedAsync(userRequest, ct);

            List<RetrievedChunk> selected = new();
            if (qdrantStore.IsConfigured)
            {
                selected = await qdrantStore.SearchAsync(repoPath, queryEmbedding, topK, ct);
            }

            if (selected.Count == 0)
            {
                selected = RetrieveFromMemory(repoPath, queryEmbedding, topK);
            }

            if (selected.Count == 0)
            {
                throw new InvalidOperationException(Constants.Messages.IndexNotFound);
            }

            data.Set("retrieved_chunks", selected);
            return data;
        });

        workflow.AddStep("PromptAndGenerate", async (data, _) =>
        {
            var chunks = data.Get<List<RetrievedChunk>>("retrieved_chunks") ?? new();
            var retrievedContext = string.Join("\n\n", chunks.Select((c, i) =>
                $"[{i + 1}] {c.FilePath}:{c.StartLine}-{c.EndLine} (score={c.Score:F4})\n{c.Snippet}"));

            var prompt = Constants.Prompts.PairProgrammerPrompt
                .Replace("{{task_type}}", data.GetString("task_type") ?? "implement")
                .Replace("{{user_request}}", data.GetString("user_request") ?? string.Empty)
                .Replace("{{retrieved_context}}", retrievedContext);

            var rawResponse = await llmService.ChatAsync(
                Constants.Prompts.PairProgrammerSystemPrompt,
                prompt,
                apiKey,
                chatModel,
                endpoint,
                maxTokens: 2_500,
                ct);

            data.Set("raw_response", rawResponse);
            return data;
        });

        workflow.AddStep("ParseOutput", async (data, _) =>
        {
            var raw = data.GetString("raw_response") ?? string.Empty;
            var chunks = data.Get<List<RetrievedChunk>>("retrieved_chunks") ?? new();
            var usedContext = chunks.Select(c => c.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var parsed = TryParseStructuredResult(raw);
            if (parsed is null)
            {
                parsed = new PairProgrammerResult
                {
                    Summary = raw,
                    SummaryMarkdown = raw,
                    SummaryHtml = Markdown.ToHtml(raw, MarkdownPipeline),
                    Risks = ["Response was not valid JSON, returned as plain summary."],
                };
            }

            if (string.IsNullOrWhiteSpace(parsed.SummaryMarkdown))
            {
                parsed.SummaryMarkdown = parsed.Summary;
            }

            if (string.IsNullOrWhiteSpace(parsed.SummaryHtml))
            {
                parsed.SummaryHtml = Markdown.ToHtml(parsed.SummaryMarkdown, MarkdownPipeline);
            }

            parsed.UsedContext = usedContext;
            if (parsed.GeneratedAt == default)
            {
                parsed.GeneratedAt = DateTime.UtcNow;
            }

            data.Set("result", parsed);
            await Task.CompletedTask;
            return data;
        });

        return workflow;
    }

    private static PairProgrammerResult? TryParseStructuredResult(string raw)
    {
        var clean = StripCodeFences(raw);
        var jsonCandidate = ExtractFirstJsonObject(clean);
        if (string.IsNullOrWhiteSpace(jsonCandidate))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonCandidate);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new PairProgrammerResult
            {
                Summary = GetString(root, "summary") ?? string.Empty,
                SummaryMarkdown = GetString(root, "summary_markdown", "summaryMarkdown") ?? string.Empty,
                SummaryHtml = GetString(root, "summary_html", "summaryHtml") ?? string.Empty,
                ImplementationPlan = GetStringList(root, "implementation_plan", "implementationPlan"),
                FilesToChange = GetStringList(root, "files_to_change", "filesToChange"),
                CodeBlocks = GetCodeBlocks(root),
                Risks = GetStringList(root, "risks"),
            };
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractFirstJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return string.Empty;
        }

        return text[start..(end + 1)];
    }

    private static string? GetString(JsonElement root, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static List<string> GetStringList(JsonElement root, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            return value
                .EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString() ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }

        return new List<string>();
    }

    private static List<CodeBlock> GetCodeBlocks(JsonElement root)
    {
        if (!root.TryGetProperty("code_blocks", out var value) && !root.TryGetProperty("codeBlocks", out value))
        {
            return new List<CodeBlock>();
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return new List<CodeBlock>();
        }

        var blocks = new List<CodeBlock>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var file = GetString(item, "file") ?? string.Empty;
            var language = GetString(item, "language") ?? "text";
            var content = GetString(item, "content") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(file) && string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            blocks.Add(new CodeBlock(file, language, content));
        }

        return blocks;
    }

    private static string StripCodeFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewLine = trimmed.IndexOf('\n');
        if (firstNewLine < 0)
        {
            return trimmed;
        }

        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (lastFence <= firstNewLine)
        {
            return trimmed;
        }

        return trimmed[(firstNewLine + 1)..lastFence].Trim();
    }

    private List<RetrievedChunk> RetrieveFromMemory(string repoPath, float[] queryEmbedding, int topK)
    {
        var chunks = indexStore.Get(repoPath);
        if (chunks is null || chunks.Count == 0)
        {
            return new List<RetrievedChunk>();
        }

        return chunks
            .Select(c => new RetrievedChunk(
                c.FilePath,
                c.Text,
                c.StartLine,
                c.EndLine,
                CosineSimilarity(queryEmbedding, c.Embedding)))
            .OrderByDescending(c => c.Score)
            .Take(Math.Clamp(topK, 1, 20))
            .ToList();
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        var len = Math.Min(a.Length, b.Length);
        if (len == 0)
        {
            return 0;
        }

        double dot = 0;
        double magA = 0;
        double magB = 0;

        for (var i = 0; i < len; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        if (magA <= 0 || magB <= 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }
}
