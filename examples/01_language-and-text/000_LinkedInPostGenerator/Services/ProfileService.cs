using System.Text.Json;
using System.Text.Json.Serialization;

namespace _000_LinkedInPostGenerator.Services;

public class ProfileService(IWebHostEnvironment env, ILogger<ProfileService> logger)
{
    private readonly ILogger<ProfileService> _logger = logger;
    private readonly string _profilePath = Path.Combine(env.ContentRootPath, "profile.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public AuthorProfile Load()
    {
        try
        {
            if (!File.Exists(_profilePath))
                return new AuthorProfile();

            var json = File.ReadAllText(_profilePath);
            return JsonSerializer.Deserialize<AuthorProfile>(json, JsonOpts) ?? new AuthorProfile();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read profile.json — using defaults");
            return new AuthorProfile();
        }
    }

    public void Save(AuthorProfile profile)
    {
        var json = JsonSerializer.Serialize(profile, JsonOpts);
        File.WriteAllText(_profilePath, json);
    }

    public string BuildSystemPrompt(AuthorProfile profile)
    {
        var previousPosts = profile.PreviousPostReferences.Count > 0
            ? string.Join("\n- ", profile.PreviousPostReferences.Prepend(string.Empty))
            : "(none provided)";

        return string.Format(
            Constants.Prompts.SystemPromptTemplate,
            string.IsNullOrWhiteSpace(profile.MyProfile) ? "(not set)" : profile.MyProfile,
            string.IsNullOrWhiteSpace(profile.WritingGuidelines) ? "(not set)" : profile.WritingGuidelines,
            previousPosts);
    }
}

// ── Author profile model ──────────────────────────────────────────────────────

public class AuthorProfile
{
    [JsonPropertyName("myProfile")]
    public string MyProfile { get; set; } = string.Empty;

    [JsonPropertyName("writingGuidelines")]
    public string WritingGuidelines { get; set; } = string.Empty;

    [JsonPropertyName("previousPostReferences")]
    public List<string> PreviousPostReferences { get; set; } = new();

    [JsonPropertyName("defaultRole")]
    public string DefaultRole { get; set; } = "developer";

    [JsonPropertyName("defaultAudience")]
    public string DefaultAudience { get; set; } = "engineers";

    [JsonPropertyName("defaultMaxChars")]
    public int DefaultMaxChars { get; set; } = 1300;
}
