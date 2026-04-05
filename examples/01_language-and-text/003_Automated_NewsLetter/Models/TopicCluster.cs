namespace _003_Automated_NewsLetter.Models;

/// <summary>A topic cluster grouping related articles, with a relevance score against the subscriber profile.</summary>
public class TopicCluster
{
    public string             Label           { get; set; } = string.Empty;
    public List<ClusteredArticle> Articles    { get; set; } = new();
    public double             RelevanceScore  { get; set; }
    public string             EnrichedContent { get; set; } = string.Empty;
}
