namespace _036_AI_Pair_Programmer.Models;

public sealed class QueryRequest
{
    public string RepoPath { get; set; } = string.Empty;
    public string UserRequest { get; set; } = string.Empty;
    public int TopK { get; set; } = 8;
    public string TaskType { get; set; } = "implement";
}
