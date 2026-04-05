using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using _003_Automated_NewsLetter.Models;
using _003_Automated_NewsLetter.Services;

namespace _003_Automated_NewsLetter.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NewsletterController : ControllerBase
{
    private readonly ILogger<NewsletterController> _logger;
    private readonly NewsletterWorkflowService _workflowService;
    private readonly SubscriberProfileService _profileService;
    private readonly FeedConfigService _feedConfigService;
    private readonly RssFeedService _rssFeedService;

    public NewsletterController(
        ILogger<NewsletterController> logger,
        NewsletterWorkflowService workflowService,
        SubscriberProfileService profileService,
        FeedConfigService feedConfigService,
        RssFeedService rssFeedService)
    {
        _logger          = logger;
        _workflowService = workflowService;
        _profileService  = profileService;
        _feedConfigService = feedConfigService;
        _rssFeedService  = rssFeedService;
    }

    /// <summary>
    /// Runs the newsletter pipeline and streams the output token-by-token via SSE.
    /// Each token is delivered as: <c>data: {"delta":"…"}\n\n</c>
    /// Errors are delivered as:   <c>data: {"error":"…"}\n\n</c>
    /// Completion is signalled:   <c>data: [DONE]\n\n</c>
    /// </summary>
    [HttpPost("generate")]
    public async Task Generate([FromBody] GenerateRequest request, CancellationToken ct)
    {
        Response.Headers["Content-Type"]      = "text/event-stream";
        Response.Headers["Cache-Control"]     = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        // OnChunk writes each token synchronously to the SSE response body.
        // AllowSynchronousIO = true is set in Program.cs.
        Action<string> onChunk = chunk =>
        {
            var bytes = Encoding.UTF8.GetBytes(
                $"data: {JsonSerializer.Serialize(new { delta = chunk })}\n\n");
            Response.Body.Write(bytes);
            Response.Body.Flush();
        };

        var result = await _workflowService.RunPipelineAsync(onChunk, ct);

        if (!result.IsSuccess)
        {
            await WriteSseErrorAsync(result.ErrorMessage ?? "Pipeline failed.", ct);
            return;
        }

        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes("data: [DONE]\n\n"), ct);
        await Response.Body.FlushAsync(ct);
    }

    /// <summary>Returns the most recently generated newsletter as JSON.</summary>
    [HttpGet("latest")]
    public IActionResult GetLatest()
    {
        var newsletter = _workflowService.LatestNewsletter;
        if (newsletter is null)
            return NotFound(new { error = "No newsletter has been generated yet." });

        return Ok(newsletter);
    }

    // ── Subscriber Profile ────────────────────────────────────────────────────

    /// <summary>Returns the current subscriber interest profile.</summary>
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile() =>
        Ok(await _profileService.GetProfileAsync());

    /// <summary>Saves the subscriber interest profile.</summary>
    [HttpPost("profile")]
    public async Task<IActionResult> SaveProfile([FromBody] SubscriberProfile profile)
    {
        await _profileService.SaveProfileAsync(profile);
        return Ok(new { message = "Profile saved." });
    }

    // ── Feed Management ───────────────────────────────────────────────────────

    /// <summary>Returns the current list of configured RSS feed URLs.</summary>
    [HttpGet("feeds")]
    public async Task<IActionResult> GetFeeds() =>
        Ok(await _feedConfigService.GetFeedsAsync());

    /// <summary>Saves the RSS feed list.</summary>
    [HttpPost("feeds")]
    public async Task<IActionResult> SaveFeeds([FromBody] List<string> feeds)
    {
        // Validate URLs to prevent SSRF by allowing only http/https schemes
        var invalid = feeds.Where(f =>
        {
            if (!Uri.TryCreate(f, UriKind.Absolute, out var uri)) return true;
            return uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps;
        }).ToList();

        if (invalid.Count > 0)
            return BadRequest(new { error = "All feeds must be valid http/https URLs.", invalid });

        await _feedConfigService.SaveFeedsAsync(feeds);
        return Ok(new { message = "Feeds saved." });
    }

    /// <summary>Tests a single RSS feed URL and returns the article count.</summary>
    [HttpPost("test-feed")]
    public async Task<IActionResult> TestFeed([FromBody] TestFeedRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            return BadRequest(new { error = "URL is required." });

        // Validate URL scheme to prevent SSRF
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return BadRequest(new { error = "Only http/https URLs are allowed." });

        var settings = _feedConfigService.GetSettings();
        var articles = await _rssFeedService.FetchArticlesAsync(
            new[] { request.Url }, 50, settings.MaxAgeDays, ct);

        var titles = articles.Select(a => a.Title).ToList();
        return Ok(new { articleCount = articles.Count, titles });
    }

    /// <summary>Health check endpoint.</summary>
    [HttpGet("health")]
    public IActionResult Health() =>
        Ok(new { status = "healthy", service = Constants.AppName, timestamp = DateTime.UtcNow });

    private async Task WriteSseErrorAsync(string message, CancellationToken ct)
    {
        var payload = $"data: {JsonSerializer.Serialize(new { error = message })}\n\n";
        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(payload), ct);
        await Response.Body.FlushAsync(ct);
    }
}

// ── Request / Response models ─────────────────────────────────────────────────

public class GenerateRequest
{
    public string SubscriberId { get; set; } = "default";
    public bool   ForceRefresh { get; set; } = true;
}

public class TestFeedRequest
{
    public string Url { get; set; } = string.Empty;
}


