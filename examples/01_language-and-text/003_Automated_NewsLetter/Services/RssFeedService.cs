using System.Xml.Linq;
using _003_Automated_NewsLetter.Models;

namespace _003_Automated_NewsLetter.Services;

/// <summary>
/// Fetches and parses RSS/Atom feeds into <see cref="RawArticle"/> objects.
/// Uses XDocument to avoid external syndication packages — works with both
/// RSS 2.0 and Atom 1.0 feed formats.
/// </summary>
public class RssFeedService
{
    private readonly HttpClient _http;
    private readonly ILogger<RssFeedService> _logger;

    public RssFeedService(HttpClient http, ILogger<RssFeedService> logger)
    {
        _http   = http;
        _logger = logger;
    }

    /// <summary>
    /// Fetches articles from all configured <paramref name="feedUrls"/> concurrently,
    /// caps the total at <paramref name="maxArticles"/>, and filters out articles older
    /// than <paramref name="maxAgeDays"/> days.
    /// </summary>
    public async Task<List<RawArticle>> FetchArticlesAsync(
        IEnumerable<string> feedUrls,
        int maxArticles,
        int maxAgeDays,
        CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-maxAgeDays);
        var tasks  = feedUrls.Select(url => FetchFeedAsync(url, cutoff, ct));
        var results = await Task.WhenAll(tasks);

        return results
            .SelectMany(r => r)
            .OrderByDescending(a => a.PubDate)
            .Take(maxArticles)
            .ToList();
    }

    private async Task<List<RawArticle>> FetchFeedAsync(string feedUrl, DateTime cutoff, CancellationToken ct)
    {
        try
        {
            var xml = await _http.GetStringAsync(feedUrl, ct);
            var doc = XDocument.Parse(xml);

            // Try RSS 2.0 first, fall back to Atom 1.0
            return doc.Root?.Name.LocalName == "feed"
                ? ParseAtom(doc, feedUrl, cutoff)
                : ParseRss(doc, feedUrl, cutoff);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch feed {Url} — skipping", feedUrl);
            return new List<RawArticle>();
        }
    }

    private static List<RawArticle> ParseRss(XDocument doc, string feedUrl, DateTime cutoff)
    {
        var articles = new List<RawArticle>();
        var channel  = doc.Descendants("channel").FirstOrDefault();
        if (channel is null) return articles;

        foreach (var item in channel.Elements("item"))
        {
            var pubDate = ParseDate(item.Element("pubDate")?.Value);
            if (pubDate < cutoff) continue;

            var mediaContent = item.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "content" && e.Attribute("url") != null);
            var enclosure = item.Element("enclosure");

            articles.Add(new RawArticle
            {
                Title      = item.Element("title")?.Value.Trim()       ?? string.Empty,
                Url        = item.Element("link")?.Value.Trim()        ?? string.Empty,
                Snippet    = StripHtml(item.Element("description")?.Value ?? string.Empty),
                ImageUrl   = mediaContent?.Attribute("url")?.Value     ??
                             enclosure?.Attribute("url")?.Value        ?? string.Empty,
                PubDate    = pubDate,
                SourceFeed = feedUrl
            });
        }

        return articles;
    }

    private static List<RawArticle> ParseAtom(XDocument doc, string feedUrl, DateTime cutoff)
    {
        var articles = new List<RawArticle>();
        XNamespace ns = "http://www.w3.org/2005/Atom";

        foreach (var entry in doc.Descendants(ns + "entry"))
        {
            var pubDate = ParseDate(entry.Element(ns + "published")?.Value
                       ?? entry.Element(ns + "updated")?.Value);
            if (pubDate < cutoff) continue;

            var link = entry.Elements(ns + "link")
                .FirstOrDefault(l => l.Attribute("rel")?.Value != "related")
                ?.Attribute("href")?.Value ?? string.Empty;

            articles.Add(new RawArticle
            {
                Title      = entry.Element(ns + "title")?.Value.Trim()   ?? string.Empty,
                Url        = link,
                Snippet    = StripHtml(entry.Element(ns + "summary")?.Value
                           ?? entry.Element(ns + "content")?.Value       ?? string.Empty),
                PubDate    = pubDate,
                SourceFeed = feedUrl
            });
        }

        return articles;
    }

    private static DateTime ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return DateTime.MinValue;
        return DateTime.TryParse(raw, out var dt) ? dt.ToUniversalTime() : DateTime.MinValue;
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
    }
}
