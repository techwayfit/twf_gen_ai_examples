namespace _030_RFPComplianceEngine.Services;

public class ChunkingService
{
    private const int DefaultChunkSize    = 400;
    private const int DefaultChunkOverlap = 50;

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
