namespace _036_AI_Pair_Programmer.Services;

/// <summary>
/// Interface for embedding text into vector representations.
/// Implementations can use different providers (OpenAI, local models, etc.)
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Generate an embedding vector for the given text.
    /// </summary>
    /// <param name="text">Text to embed</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Float array representing the embedding vector</returns>
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Gets the dimension of the embedding vectors produced by this service.
    /// </summary>
    int EmbeddingDimension { get; }
}
