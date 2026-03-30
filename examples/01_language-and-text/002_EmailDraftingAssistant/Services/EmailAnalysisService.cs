using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using _002_EmailDraftingAssistant.Models;

namespace _002_EmailDraftingAssistant.Services;

/// <summary>
/// Uses Claude API to derive TicketType, Sentiment, UrgencyLevel, Product etc.
/// from raw email content. Results are cached in-memory to avoid re-calling
/// the API for the same MessageId within a session.
/// </summary>
public class EmailAnalysisService
{
    private readonly HttpClient _http;
    private readonly Dictionary<string, EmailAnalysis>  _emailCache  = new();
    private readonly Dictionary<string, ThreadAnalysis> _threadCache = new();

    private const string ApiUrl    = "https://api.anthropic.com/v1/messages";
    private const string ApiModel  = "claude-sonnet-4-20250514";
    private const string ApiVersion= "2023-06-01";

    public EmailAnalysisService(HttpClient http) => _http = http;

    // =========================================================================
    // SINGLE EMAIL ANALYSIS
    // =========================================================================

    /// <summary>
    /// Analyse a single email — derives TicketType, Sentiment, Urgency, Product,
    /// Intent, SuggestedTone, NeedsHumanEscalation.
    /// Results are cached by MessageId.
    /// </summary>
    public async Task<EmailAnalysis> AnalyzeEmailAsync(EmailMessage email)
    {
        if (_emailCache.TryGetValue(email.MessageId, out var cached)) return cached;

        var prompt = @$"
            You are a support triage AI for TechWayFit, an IT solutions company.
            Analyse the email below and return ONLY a valid JSON object — no markdown, no preamble.

            JSON schema:
            {{
              ""ticketType"":   one of [new_ticket, follow_up, escalation, resolution, satisfaction, complaint, angry, closure, reopen, billing, unknown],
              ""urgencyLevel"": one of [low, medium, high, critical],
              ""sentiment"":    one of [positive, neutral, frustrated, angry],
              ""product"":      string — the IT product or service mentioned, or "" if none,
              ""intent"":       string — one sentence describing what the sender wants,
              ""suggestedTone"":string — one of [formal, apologetic, empathetic, direct, grateful],
              ""needsHumanEscalation"": boolean
            }}

            Email Subject : {email.Subject}
            Email Body    :
            {email.Body}
            ";

        var raw  = await CallClaudeAsync(prompt);
        var json = ExtractJson(raw);
        var doc  = JsonDocument.Parse(json).RootElement;

        var analysis = new EmailAnalysis
        {
            MessageId            = email.MessageId,
            TicketType           = ParseEnum<TicketType>(doc,   "ticketType",   TicketType.Unknown),
            UrgencyLevel         = ParseEnum<UrgencyLevel>(doc, "urgencyLevel", UrgencyLevel.Low),
            Sentiment            = ParseEnum<Sentiment>(doc,    "sentiment",    Sentiment.Neutral),
            Product              = doc.TryGetProperty("product",       out var p)  ? p.GetString() ?? "" : "",
            Intent               = doc.TryGetProperty("intent",        out var iv) ? iv.GetString() ?? "" : "",
            SuggestedTone        = doc.TryGetProperty("suggestedTone", out var st) ? st.GetString() ?? "" : "",
            NeedsHumanEscalation = doc.TryGetProperty("needsHumanEscalation", out var nh) && nh.GetBoolean(),
            RawJson              = json
        };

        _emailCache[email.MessageId] = analysis;
        return analysis;
    }

    /// <summary>Analyse a batch of emails concurrently (max 5 parallel to respect rate limits).</summary>
    public async Task<List<AnnotatedEmail>> AnalyzeBatchAsync(
        List<EmailMessage> emails, int parallelism = 5)
    {
        var semaphore = new SemaphoreSlim(parallelism);
        var tasks = emails.Select(async email =>
        {
            await semaphore.WaitAsync();
            try
            {
                var analysis = await AnalyzeEmailAsync(email);
                return new AnnotatedEmail { Email = email, Analysis = analysis };
            }
            finally { semaphore.Release(); }
        });

        return (await Task.WhenAll(tasks)).ToList();
    }

    // =========================================================================
    // THREAD / TICKET ANALYSIS
    // =========================================================================

    /// <summary>
    /// Analyse an entire ticket thread to derive overall status, issue summary,
    /// escalation need, and recommended next action.
    /// Thread is summarised from all emails to stay within context limits.
    /// </summary>
    public async Task<ThreadAnalysis> AnalyzeThreadAsync(
        string ticketNumber, List<EmailMessage> thread)
    {
        if (_threadCache.TryGetValue(ticketNumber, out var cached)) return cached;

        // Summarise thread to avoid huge prompts — keep last 6 emails max
        var relevant = thread.OrderBy(e => e.Date).TakeLast(6).ToList();
        var threadText = string.Join("\n---\n", relevant.Select(e =>
            $"[{e.Date:dd-MMM-yyyy HH:mm}] From: {e.SenderName}\nSubject: {e.Subject}\n{e.Body}"));

        var prompt = @$"
            You are a senior support analyst at TechWayFit, an IT solutions company.
            Review this customer ticket thread and return ONLY a valid JSON object — no markdown.

            JSON schema:
            {{
              ""currentStatus"":    one of [new_ticket, follow_up, escalation, resolution, satisfaction, complaint, angry, closure, reopen, billing, unknown],
              ""ticketStatus"":     one of [open, closed],
              ""urgencyLevel"":     one of [low, medium, high, critical],
              ""overallSentiment"": one of [positive, neutral, frustrated, angry],
              ""product"":          string — IT product/service involved, or "",
              ""issueSummary"":     string — 2-3 sentence summary of the issue and current state,
              ""suggestedAction"":  string — what the support team should do next,
              ""needsEscalation"":  boolean
            }}

            Ticket: {ticketNumber}
            Thread ({thread.Count} emails):
            {threadText}
            ";

        var raw  = await CallClaudeAsync(prompt);
        var json = ExtractJson(raw);
        var doc  = JsonDocument.Parse(json).RootElement;

        var analysis = new ThreadAnalysis
        {
            TicketNumber     = ticketNumber,
            CurrentStatus    = ParseEnum<TicketType>(doc,   "currentStatus",    TicketType.Unknown),
            TicketStatus     = ParseEnum<TicketStatus>(doc, "ticketStatus",     TicketStatus.Open),
            UrgencyLevel     = ParseEnum<UrgencyLevel>(doc, "urgencyLevel",     UrgencyLevel.Low),
            OverallSentiment = ParseEnum<Sentiment>(doc,    "overallSentiment", Sentiment.Neutral),
            Product          = doc.TryGetProperty("product",         out var pd) ? pd.GetString() ?? "" : "",
            IssueSummary     = doc.TryGetProperty("issueSummary",    out var s)  ? s.GetString()  ?? "" : "",
            SuggestedAction  = doc.TryGetProperty("suggestedAction", out var sa) ? sa.GetString() ?? "" : "",
            NeedsEscalation  = doc.TryGetProperty("needsEscalation", out var ne) && ne.GetBoolean(),
            EmailCount       = thread.Count,
            RawJson          = json
        };

        _threadCache[ticketNumber] = analysis;
        return analysis;
    }

    // =========================================================================
    // PRIORITY SCORING
    // =========================================================================

    /// <summary>
    /// Returns a numeric priority score (lower = higher priority) for a
    /// given analysis — used to sort the support agent work queue.
    /// </summary>
    public static int GetPriorityScore(EmailAnalysis a)
    {
        int score = a.TicketType switch
        {
            TicketType.Angry      => 10,
            TicketType.Escalation => 20,
            TicketType.Reopen     => 30,
            TicketType.Complaint  => 40,
            TicketType.FollowUp   => 50,
            TicketType.NewTicket  => 60,
            TicketType.Billing    => 70,
            _                     => 90
        };

        score += a.UrgencyLevel switch
        {
            UrgencyLevel.Critical => 0,
            UrgencyLevel.High     => 5,
            UrgencyLevel.Medium   => 10,
            _                     => 15
        };

        if (a.NeedsHumanEscalation) score -= 5;
        return score;
    }

    // =========================================================================
    // CACHE MANAGEMENT
    // =========================================================================

    public bool IsAnalyzed(string messageId)          => _emailCache.ContainsKey(messageId);
    public bool IsThreadAnalyzed(string ticketNumber) => _threadCache.ContainsKey(ticketNumber);

    public EmailAnalysis?  GetCachedAnalysis(string messageId)      => _emailCache.GetValueOrDefault(messageId);
    public ThreadAnalysis? GetCachedThreadAnalysis(string ticketNum) => _threadCache.GetValueOrDefault(ticketNum);

    public void ClearCache() { _emailCache.Clear(); _threadCache.Clear(); }

    public (int EmailsCached, int ThreadsCached) CacheStats()
        => (_emailCache.Count, _threadCache.Count);

    // =========================================================================
    // PRIVATE HELPERS
    // =========================================================================

    private async Task<string> CallClaudeAsync(string userPrompt)
    {
        var body = JsonSerializer.Serialize(new
        {
            model      = ApiModel,
            max_tokens = 1000,
            messages   = new[] { new { role = "user", content = userPrompt } }
        });

        var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("anthropic-version", ApiVersion);

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc  = JsonDocument.Parse(json).RootElement;

        return doc.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
    }

    private static string ExtractJson(string raw)
    {
        // Strip any accidental markdown fences
        raw = raw.Trim();
        if (raw.StartsWith("```")) raw = raw.Split('\n', 2)[1];
        if (raw.EndsWith("```"))   raw = raw[..^3];
        return raw.Trim();
    }

    private static TEnum ParseEnum<TEnum>(JsonElement doc, string key, TEnum fallback)
        where TEnum : struct, Enum
    {
        if (!doc.TryGetProperty(key, out var val)) return fallback;
        var str = val.GetString() ?? "";

        // Convert snake_case → PascalCase for matching
        var pascal = string.Concat(str.Split('_').Select(s =>
            s.Length > 0 ? char.ToUpper(s[0]) + s[1..] : s));

        return Enum.TryParse<TEnum>(pascal, ignoreCase: true, out var result)
            ? result : fallback;
    }
}
