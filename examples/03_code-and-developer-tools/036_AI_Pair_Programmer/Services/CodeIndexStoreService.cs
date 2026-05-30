using System.Collections.Concurrent;

namespace _036_AI_Pair_Programmer.Services;

public sealed class CodeIndexStoreService
{
    private readonly ConcurrentDictionary<string, List<IndexedChunk>> _indices = new(StringComparer.OrdinalIgnoreCase);

    public void Upsert(string repoPath, List<IndexedChunk> chunks)
    {
        _indices[Normalize(repoPath)] = chunks;
    }

    public IReadOnlyList<IndexedChunk>? Get(string repoPath)
    {
        return _indices.TryGetValue(Normalize(repoPath), out var chunks) ? chunks : null;
    }

    public bool HasIndex(string repoPath)
    {
        return _indices.ContainsKey(Normalize(repoPath));
    }

    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
}

public sealed record IndexedChunk(
    string FilePath,
    string Text,
    int StartLine,
    int EndLine,
    float[] Embedding);
