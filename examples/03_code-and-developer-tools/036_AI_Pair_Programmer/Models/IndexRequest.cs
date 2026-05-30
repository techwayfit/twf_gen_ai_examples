namespace _036_AI_Pair_Programmer.Models;

public sealed class IndexRequest
{
    public string RepoPath { get; set; } = string.Empty;
    public List<string> Languages { get; set; } = new();
    public int MaxChunkTokens { get; set; } = 600;
    public int MaxFiles { get; set; } = 250;
}
