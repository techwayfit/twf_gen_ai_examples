namespace _003_Automated_NewsLetter.Models;

/// <summary>Raw article fetched from an RSS/Atom feed before clustering.</summary>
public class RawArticle
{
    public string Title      { get; set; } = string.Empty;
    public string Url        { get; set; } = string.Empty;
    public string Snippet    { get; set; } = string.Empty;
    public string ImageUrl   { get; set; } = string.Empty;
    public DateTime PubDate  { get; set; }
    public string SourceFeed { get; set; } = string.Empty;
}
