namespace _036_AI_Pair_Programmer.Models;

public sealed record RetrievedChunk(
    string FilePath,
    string Snippet,
    int StartLine,
    int EndLine,
    double Score);
