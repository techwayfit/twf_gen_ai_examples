using ElBruno.LocalEmbeddings;
using Microsoft.Extensions.AI;

namespace _036_AI_Pair_Programmer.Services;

/// <summary>
/// Local embedding service using ElBruno.LocalEmbeddings for offline embedding generation.
/// Uses the default sentence-transformers/all-MiniLM-L6-v2 model (384 dimensions).
/// </summary>
public class LocalEmbeddingService : IEmbeddingService, IAsyncDisposable
{
    private readonly LocalEmbeddingGenerator _generator;
    private readonly int _embeddingDimension;
    private bool _disposed;

    private LocalEmbeddingService(LocalEmbeddingGenerator generator, int embeddingDimension)
    {
        _generator = generator;
        _embeddingDimension = embeddingDimension;
    }

    public static async Task<LocalEmbeddingService> CreateAsync(IConfiguration configuration)
    {
        var embeddingDimension = configuration.GetValue<int>("LocalEmbeddings:EmbeddingDimension", 384);

        // Use default model: sentence-transformers/all-MiniLM-L6-v2
        // ElBruno.LocalEmbeddings will download it if needed
        var generator = await LocalEmbeddingGenerator.CreateAsync();

        return new LocalEmbeddingService(generator, embeddingDimension);
    }

    public int EmbeddingDimension => _embeddingDimension;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LocalEmbeddingService));

        ct.ThrowIfCancellationRequested();

        // Generate embedding using the async API
        var embedding = await _generator.GenerateEmbeddingAsync(text, cancellationToken: ct);
        return embedding.Vector.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        await _generator.DisposeAsync();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
