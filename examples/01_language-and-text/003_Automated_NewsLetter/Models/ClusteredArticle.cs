namespace _003_Automated_NewsLetter.Models;

/// <summary>Article after the single-call clustering LLM step — one entry per article in the JSON array.</summary>
public class ClusteredArticle
{
    public string Url      { get; set; } = string.Empty;
    public string Title    { get; set; } = string.Empty;
    public string Cluster  { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
}
