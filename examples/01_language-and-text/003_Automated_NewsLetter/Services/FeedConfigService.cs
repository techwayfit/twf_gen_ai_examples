using System.Text.Json;
using _003_Automated_NewsLetter.Models;

namespace _003_Automated_NewsLetter.Services;

/// <summary>Manages the RSS feed list — seeds from appsettings, then persists overrides to disk.</summary>
public class FeedConfigService
{
    private readonly string _filePath;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FeedConfigService> _logger;
    private static readonly JsonSerializerOptions JsonOpts =
        new() { WriteIndented = true };

    public FeedConfigService(
        IWebHostEnvironment env,
        IConfiguration configuration,
        ILogger<FeedConfigService> logger)
    {
        _configuration = configuration;
        _logger        = logger;
        var dir        = Path.Combine(env.ContentRootPath, "AppData");
        Directory.CreateDirectory(dir);
        _filePath      = Path.Combine(dir, "feed_config.json");
    }

    /// <summary>Returns the current list of RSS feed URLs (disk overrides appsettings).</summary>
    public async Task<List<string>> GetFeedsAsync()
    {
        if (File.Exists(_filePath))
        {
            try
            {
                var json  = await File.ReadAllTextAsync(_filePath);
                var feeds = JsonSerializer.Deserialize<List<string>>(json, JsonOpts);
                if (feeds is { Count: > 0 }) return feeds;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read feed_config.json — falling back to appsettings");
            }
        }

        return _configuration
            .GetSection("NewsletterSettings:Feeds")
            .Get<List<string>>() ?? new List<string>();
    }

    public async Task SaveFeedsAsync(List<string> feeds)
    {
        var json = JsonSerializer.Serialize(feeds, JsonOpts);
        await File.WriteAllTextAsync(_filePath, json);
    }

    public NewsletterSettings GetSettings() =>
        _configuration.GetSection("NewsletterSettings").Get<NewsletterSettings>()
        ?? new NewsletterSettings();
}

/// <summary>Bound from the <c>NewsletterSettings</c> configuration section.</summary>
public class NewsletterSettings
{
    public List<string> Feeds              { get; set; } = new();
    public int          MaxArticles        { get; set; } = 50;
    public int          MaxAgeDays         { get; set; } = 7;
    public int          RelevanceThreshold { get; set; } = 4;
    public string       Schedule           { get; set; } = "0 8 * * 1";
}
