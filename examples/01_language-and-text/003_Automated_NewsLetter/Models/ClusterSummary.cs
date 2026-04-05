namespace _003_Automated_NewsLetter.Models;

/// <summary>LLM-generated summary for a single relevant topic cluster.</summary>
public class ClusterSummary
{
    public string       Label         { get; set; } = string.Empty;
    public string       Summary       { get; set; } = string.Empty;
    public List<string> KeyTakeaways  { get; set; } = new();
    public string       TopArticleUrl { get; set; } = string.Empty;
    public string       ImageUrl      { get; set; } = string.Empty;
}
