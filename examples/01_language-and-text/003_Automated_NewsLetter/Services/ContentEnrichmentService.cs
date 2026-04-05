using HtmlAgilityPack;
using _003_Automated_NewsLetter.Models;

namespace _003_Automated_NewsLetter.Services;

/// <summary>
/// Scrapes the full article text from a page URL using HtmlAgilityPack.
/// Only called after relevance scoring — irrelevant clusters are never fetched.
/// </summary>
public class ContentEnrichmentService
{
    private readonly HttpClient _http;
    private readonly ILogger<ContentEnrichmentService> _logger;

    // Tags that typically contain article body content
    private static readonly string[] ContentSelectors =
    [
        "//article",
        "//div[contains(@class,'article-body')]",
        "//div[contains(@class,'post-content')]",
        "//div[contains(@class,'entry-content')]",
        "//div[contains(@class,'story-body')]",
        "//main",
    ];

    public ContentEnrichmentService(HttpClient http, ILogger<ContentEnrichmentService> logger)
    {
        _http   = http;
        _logger = logger;
    }

    /// <summary>
    /// Scrapes full article text for every article in <paramref name="articles"/>.
    /// Returns at most 1500 characters of clean text per article to control token spend.
    /// Failures are silently skipped — the snippet is used as fallback.
    /// </summary>
    public async Task<string> EnrichClusterAsync(
        IEnumerable<ClusteredArticle> articles,
        CancellationToken ct = default)
    {
        var parts = new List<string>();

        foreach (var article in articles)
        {
            var text = await ScrapeTextAsync(article.Url, ct);
            if (string.IsNullOrWhiteSpace(text)) continue;

            var truncated = text.Length > 1500 ? text[..1500] + "…" : text;
            parts.Add($"## {article.Title}\nURL: {article.Url}\n{truncated}");
        }

        return string.Join("\n\n---\n\n", parts);
    }

    private async Task<string> ScrapeTextAsync(string url, CancellationToken ct)
    {
        try
        {
            var html = await _http.GetStringAsync(url, ct);
            var doc  = new HtmlDocument();
            doc.LoadHtml(html);

            // Remove script and style nodes
            foreach (var node in doc.DocumentNode.SelectNodes("//script|//style") ?? Enumerable.Empty<HtmlNode>())
                node.Remove();

            // Try content selectors in order
            foreach (var xpath in ContentSelectors)
            {
                var node = doc.DocumentNode.SelectSingleNode(xpath);
                if (node is null) continue;

                var text = HtmlEntity.DeEntitize(node.InnerText);
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
                if (text.Length > 200) return text;
            }

            // Fallback: whole body text
            var body = doc.DocumentNode.SelectSingleNode("//body");
            if (body is not null)
            {
                var text = HtmlEntity.DeEntitize(body.InnerText);
                return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
            }

            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to scrape {Url}", url);
            return string.Empty;
        }
    }
}
