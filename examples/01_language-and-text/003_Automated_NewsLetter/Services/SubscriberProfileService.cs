using System.Text;
using System.Text.Json;
using _003_Automated_NewsLetter.Models;

namespace _003_Automated_NewsLetter.Services;

/// <summary>Loads and saves the subscriber interest profile to a JSON file on disk.</summary>
public class SubscriberProfileService
{
    private readonly string _filePath;
    private readonly ILogger<SubscriberProfileService> _logger;
    private static readonly JsonSerializerOptions JsonOpts =
        new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public SubscriberProfileService(IWebHostEnvironment env, ILogger<SubscriberProfileService> logger)
    {
        _logger   = logger;
        var dir   = Path.Combine(env.ContentRootPath, "AppData");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "subscriber_profile.json");
    }

    public async Task<SubscriberProfile> GetProfileAsync()
    {
        if (!File.Exists(_filePath))
            return new SubscriberProfile();

        try
        {
            var json    = await File.ReadAllTextAsync(_filePath);
            return JsonSerializer.Deserialize<SubscriberProfile>(json, JsonOpts)
                ?? new SubscriberProfile();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read subscriber profile — using defaults");
            return new SubscriberProfile();
        }
    }

    public async Task SaveProfileAsync(SubscriberProfile profile)
    {
        var json = JsonSerializer.Serialize(profile, JsonOpts);
        await File.WriteAllTextAsync(_filePath, json);
    }
}
