namespace _036_AI_Pair_Programmer.Models;

public sealed record CodeBlock(
    string File,
    string Language,
    string Content);

public sealed class PairProgrammerResult
{
    public string Summary { get; set; } = string.Empty;
    public string SummaryMarkdown { get; set; } = string.Empty;
    public string SummaryHtml { get; set; } = string.Empty;
    public List<string> ImplementationPlan { get; set; } = new();
    public List<string> FilesToChange { get; set; } = new();
    public List<CodeBlock> CodeBlocks { get; set; } = new();
    public List<string> Risks { get; set; } = new();
    public List<string> UsedContext { get; set; } = new();
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}
