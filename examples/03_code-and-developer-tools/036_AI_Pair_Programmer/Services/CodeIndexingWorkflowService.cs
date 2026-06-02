using _036_AI_Pair_Programmer.Models;
using TwfAiFramework.Core;
using TwfAiFramework.Core.Extensions;
using TwfAiFramework.Nodes.Data;

namespace _036_AI_Pair_Programmer.Services;

public sealed class CodeIndexingWorkflowService(
    ILogger<CodeIndexingWorkflowService> logger,
    CodeChunkerService chunkerService,
    CodeIndexStoreService indexStore,
    QdrantVectorStoreService qdrantStore,
    IEmbeddingService embeddingService,
    LlmService llmService)
{
    public async Task<IndexResult> RunAsync(
        IndexRequest request,
        string apiKey,
        CancellationToken ct = default)
    {
        return await RunAsync(request, apiKey, null, ct);
    }

    public async Task<IndexResult> RunAsync(
        IndexRequest request,
        string apiKey,
        IProgress<(string operation, string? currentFile, int processedChunks, int totalChunks)>? progress,
        CancellationToken ct = default)
    {
        progress?.Report(("Initializing", null, 0, 0));
        var workflow = BuildWorkflow(request, apiKey, progress, ct);

        var input = WorkflowData
            .From("repo_path", request.RepoPath)
            .Set("max_files", request.MaxFiles.ToString())
            .Set("max_chunk_tokens", request.MaxChunkTokens.ToString());

        var context = new WorkflowContext("CodeIndexing", logger);
        var result = await workflow.RunAsync(input, context, ct);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.ErrorMessage ?? "Code indexing failed.");
        }

        var indexedFiles = result.Data.Get<int>("indexed_files");
        var chunkCount = result.Data.Get<int>("chunk_count");

        progress?.Report(("Completed", null, chunkCount, chunkCount));
        return new IndexResult(indexedFiles, chunkCount, DateTime.UtcNow);
    }

    private Workflow BuildWorkflow(
        IndexRequest request,
        string apiKey,
        IProgress<(string operation, string? currentFile, int processedChunks, int totalChunks)>? progress,
        CancellationToken ct)
    {
        var workflow = Workflow.Create("CodebaseIndexing").UseLogger(logger);

        workflow.AddNode(new FilterNode("ValidateIndexInput")
            .RequireNonEmpty("repo_path"));

        workflow.AddStep("ChunkCodeFiles", async (data, _) =>
        {
            progress?.Report(("Chunking code files", null, 0, 0));
            var repoPath = data.GetString("repo_path") ?? string.Empty;
            var chunks = chunkerService.BuildChunks(
                repoPath,
                request.Languages,
                request.MaxChunkTokens,
                request.MaxFiles);

            data.Set("raw_chunks", chunks);
            data.Set("chunk_count", chunks.Count);
            data.Set("indexed_files", chunks.Select(c => c.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            data.Set("processed_chunks", 0);

            progress?.Report(("Ready to embed", null, 0, chunks.Count));
            await Task.CompletedTask;
            return data;
        });

        workflow.ForEach("raw_chunks", "chunk", async (loop) =>
            loop.AddStep("EmbedChunk", async (data, _) =>
                {
                    var chunk = data.Get<RawCodeChunk>("__loop_item__");
                    var embedding = await embeddingService.EmbedAsync(chunk.Text, ct);

                    var indexedChunk = new IndexedChunk(chunk.FilePath, chunk.Text, chunk.StartLine, chunk.EndLine, embedding);
                    data.Set("indexedChunk", indexedChunk);
                    return data;
                })
                .AddStep("UpsertChunk", async (data, _) =>
                {
                    var repoPath = data.GetString("repo_path") ?? string.Empty;
                    var chunk = data.Get<IndexedChunk>("indexedChunk");
                    if (chunk != null)
                    {   
                        indexStore.Upsert(repoPath, [chunk]);

                        if (qdrantStore.IsConfigured)
                        {
                            await qdrantStore.UpsertAsync(repoPath, [chunk], ct);
                        }
                    }
                    return data;
                }));

        workflow.AddStep("EmbedAndUpsert", async (data, _) =>
        {
            var repoPath = data.GetString("repo_path") ?? string.Empty;
            var chunks = data.Get<IReadOnlyList<RawCodeChunk>>("raw_chunks") ?? [];
            var indexedChunks = new List<IndexedChunk>(chunks.Count);
            var totalChunks = chunks.Count;
            var processedCount = 0;

            foreach (var chunk in chunks)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(("Embedding", chunk.FilePath, processedCount, totalChunks));

                var embedding = await embeddingService.EmbedAsync(chunk.Text, ct);
                indexedChunks.Add(new IndexedChunk(chunk.FilePath, chunk.Text, chunk.StartLine, chunk.EndLine, embedding));

                processedCount++;
                progress?.Report(("Embedded", chunk.FilePath, processedCount, totalChunks));
            }

            progress?.Report(("Storing embeddings", null, processedCount, totalChunks));
            indexStore.Upsert(repoPath, indexedChunks);

            if (qdrantStore.IsConfigured)
            {
                await qdrantStore.UpsertAsync(repoPath, indexedChunks, ct);
            }

            return data;
        });

        return workflow;
    }
}
