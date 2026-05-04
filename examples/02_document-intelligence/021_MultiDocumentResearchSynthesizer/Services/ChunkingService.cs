namespace _021_MultiDocumentResearchSynthesizer.Services;

/// <summary>
/// Splits plain-text documents into overlapping token-approximate chunks.
/// </summary>
public class ChunkingService
{
    private const int DefaultChunkSize    = 400; // words (approx 512 tokens)
    private const int DefaultChunkOverlap = 50;  // words (approx 64 tokens)

    /// <summary>
    /// Splits <paramref name="text"/> into overlapping word-based chunks.
    /// Returns a list of (chunkIndex, chunkText) pairs.
    /// </summary>
    public List<(int Index, string Text)> Chunk(
        string text,
        int chunkSize    = DefaultChunkSize,
        int chunkOverlap = DefaultChunkOverlap)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<(int, string)>();

        var words   = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var results = new List<(int, string)>();
        int step    = Math.Max(1, chunkSize - chunkOverlap);
        int idx     = 0;

        for (int start = 0; start < words.Length; start += step)
        {
            var slice = words.Skip(start).Take(chunkSize).ToArray();
            if (slice.Length == 0) break;

            results.Add((idx++, string.Join(' ', slice)));
        }

        return results;
    }
}
