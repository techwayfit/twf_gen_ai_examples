namespace _003_Automated_NewsLetter.Models;

/// <summary>A fully generated newsletter — cached in memory as the latest result.</summary>
public class GeneratedNewsletter
{
    public string   MarkdownContent { get; set; } = string.Empty;
    public DateTime GeneratedAt     { get; set; } = DateTime.UtcNow;
    public int      ClusterCount    { get; set; }
    public int      ArticleCount    { get; set; }
}
