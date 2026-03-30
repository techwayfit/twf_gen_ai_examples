using _002_EmailDraftingAssistant.Models;

namespace _002_EmailDraftingAssistant.Services;

/// <summary>
/// Core email service — reads raw emails from CSV and delegates all
/// intelligence (TicketType, Sentiment, Urgency etc.) to EmailAnalysisService.
/// 
/// Pattern:
///   1. Raw queries  → operate on CSV data only (fast, no API calls)
///   2. Smart queries → call AnalysisService, then filter/sort on AI fields
/// </summary>
public class EmailService
{
    private readonly List<EmailMessage>   _emails;
    private readonly EmailAnalysisService _analysis;

    private static readonly HashSet<TicketType> OpenTypes = new()
        { TicketType.NewTicket, TicketType.FollowUp, TicketType.Escalation,
          TicketType.Reopen,    TicketType.Complaint, TicketType.Angry, TicketType.Billing };

    private static readonly HashSet<TicketType> ClosedTypes = new()
        { TicketType.Resolution, TicketType.Closure, TicketType.Satisfaction };

    public EmailService(string csvPath, EmailAnalysisService analysisService)
    {
        _emails   = new CsvEmailReader(csvPath).ReadAll();
        _analysis = analysisService;
    }

    // =========================================================================
    // 1. RAW RETRIEVAL  — no AI needed
    // =========================================================================

    public EmailMessage? GetById(string messageId)
        => _emails.FirstOrDefault(e => e.MessageId.Equals(messageId, StringComparison.OrdinalIgnoreCase));

    public List<EmailMessage> GetThread(string threadId)
        => _emails.Where(e => e.ThreadId.Equals(threadId, StringComparison.OrdinalIgnoreCase))
                  .OrderBy(e => e.Date).ToList();

    public List<EmailMessage> GetByTicket(string ticketNumber)
        => _emails.Where(e => e.TicketNumber.Equals(ticketNumber, StringComparison.OrdinalIgnoreCase))
                  .OrderBy(e => e.Date).ToList();

    public List<EmailMessage> GetBySender(string senderEmail, int max = 50)
        => _emails.Where(e => e.SenderEmail.Contains(senderEmail, StringComparison.OrdinalIgnoreCase))
                  .OrderByDescending(e => e.Date).Take(max).ToList();

    public List<EmailMessage> GetUnread(int max = 50)
        => _emails.Where(e => e.IsUnread).OrderByDescending(e => e.Date).Take(max).ToList();

    public List<EmailMessage> SearchKeyword(string keyword, int max = 50)
        => _emails.Where(e =>
                e.Subject.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                e.Body.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Date).Take(max).ToList();

    public List<EmailMessage> GetAll() => _emails;

    public List<string> GetAllTicketNumbers()
        => _emails.Select(e => e.TicketNumber).Distinct().OrderBy(t => t).ToList();

    public List<string> GetAllSenderEmails()
        => _emails.Select(e => e.SenderEmail).Distinct().OrderBy(s => s).ToList();

    // =========================================================================
    // 2. RAW FILTER  — subset without AI fields
    // =========================================================================

    public List<EmailMessage> FilterRaw(EmailFilter filter)
    {
        IEnumerable<EmailMessage> q = _emails;

        if (!string.IsNullOrWhiteSpace(filter.SenderEmail))
            q = q.Where(e => e.SenderEmail.Contains(filter.SenderEmail, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(filter.Company))
            q = q.Where(e => e.Company.Contains(filter.Company, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(filter.TicketNumber))
            q = q.Where(e => e.TicketNumber.Equals(filter.TicketNumber, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(filter.Keyword))
            q = q.Where(e => e.Subject.Contains(filter.Keyword, StringComparison.OrdinalIgnoreCase) ||
                              e.Body.Contains(filter.Keyword, StringComparison.OrdinalIgnoreCase));
        if (filter.IsUnread.HasValue)
            q = q.Where(e => e.IsUnread == filter.IsUnread.Value);
        if (filter.FromDate.HasValue)
            q = q.Where(e => e.Date >= filter.FromDate.Value);
        if (filter.ToDate.HasValue)
            q = q.Where(e => e.Date <= filter.ToDate.Value);

        return q.OrderByDescending(e => e.Date).Take(filter.MaxResults).ToList();
    }

    // =========================================================================
    // 3. SMART FILTER  — AI analysis required
    // =========================================================================

    /// <summary>
    /// Filters and sorts emails using AI-derived fields.
    /// Automatically triggers analysis for any emails not yet analysed.
    /// </summary>
    public async Task<List<AnnotatedEmail>> FilterSmartAsync(EmailFilter filter)
    {
        // Step 1: narrow down with raw filters first (cheaper)
        var rawFilter = new EmailFilter
        {
            SenderEmail  = filter.SenderEmail,
            Company      = filter.Company,
            TicketNumber = filter.TicketNumber,
            Keyword      = filter.Keyword,
            IsUnread     = filter.IsUnread,
            FromDate     = filter.FromDate,
            ToDate       = filter.ToDate,
            MaxResults   = int.MaxValue
        };
        var candidates = FilterRaw(rawFilter);

        // Step 2: analyse in batch
        var annotated = await _analysis.AnalyzeBatchAsync(candidates);

        // Step 3: apply AI-field filters
        IEnumerable<AnnotatedEmail> q = annotated;

        if (filter.TicketType.HasValue)
            q = q.Where(a => a.Analysis.TicketType == filter.TicketType.Value);
        if (filter.UrgencyLevel.HasValue)
            q = q.Where(a => a.Analysis.UrgencyLevel == filter.UrgencyLevel.Value);
        if (filter.Sentiment.HasValue)
            q = q.Where(a => a.Analysis.Sentiment == filter.Sentiment.Value);
        if (!string.IsNullOrWhiteSpace(filter.Product))
            q = q.Where(a => a.Analysis.Product.Contains(filter.Product, StringComparison.OrdinalIgnoreCase));

        return q.OrderByDescending(a => a.Email.Date)
                .Take(filter.MaxResults)
                .ToList();
    }

    // =========================================================================
    // 4. SINGLE EMAIL — analyse on demand
    // =========================================================================

    public async Task<AnnotatedEmail?> GetAnnotatedAsync(string messageId)
    {
        var email = GetById(messageId);
        if (email == null) return null;
        var analysis = await _analysis.AnalyzeEmailAsync(email);
        return new AnnotatedEmail { Email = email, Analysis = analysis };
    }

    // =========================================================================
    // 5. TICKET SUMMARY — thread analysis
    // =========================================================================

    public async Task<TicketSummary?> GetTicketSummaryAsync(string ticketNumber)
    {
        var thread = GetByTicket(ticketNumber);
        if (thread.Count == 0) return null;

        var threadAnalysis = await _analysis.AnalyzeThreadAsync(ticketNumber, thread);

        return new TicketSummary
        {
            TicketNumber = ticketNumber,
            Analysis     = threadAnalysis,
            Thread       = thread,
            FirstEmail   = thread.First(),
            LatestEmail  = thread.Last()
        };
    }

    // =========================================================================
    // 6. INBOX SMART VIEWS — AI-classified queries
    // =========================================================================

    public async Task<List<AnnotatedEmail>> GetAngryEmailsAsync(int max = 20)
        => await FilterSmartAsync(new EmailFilter
               { TicketType = TicketType.Angry, MaxResults = max });

    public async Task<List<AnnotatedEmail>> GetEscalationsAsync(int max = 20)
        => await FilterSmartAsync(new EmailFilter
               { TicketType = TicketType.Escalation, MaxResults = max });

    public async Task<List<AnnotatedEmail>> GetHighUrgencyAsync(int max = 20)
        => await FilterSmartAsync(new EmailFilter
               { UrgencyLevel = UrgencyLevel.High, MaxResults = max });

    public async Task<List<AnnotatedEmail>> GetCriticalAsync(int max = 20)
        => await FilterSmartAsync(new EmailFilter
               { UrgencyLevel = UrgencyLevel.Critical, MaxResults = max });

    public async Task<List<AnnotatedEmail>> GetNeedsEscalationAsync(int max = 20)
    {
        // Get candidates and filter on NeedsHumanEscalation flag
        var all = await _analysis.AnalyzeBatchAsync(_emails.Take(100).ToList());
        return all.Where(a => a.Analysis.NeedsHumanEscalation)
                  .OrderByDescending(a => a.Email.Date)
                  .Take(max).ToList();
    }

    public async Task<List<AnnotatedEmail>> GetSatisfactionEmailsAsync(int max = 20)
        => await FilterSmartAsync(new EmailFilter
               { TicketType = TicketType.Satisfaction, MaxResults = max });

    public async Task<List<AnnotatedEmail>> GetReopenedAsync(int max = 20)
        => await FilterSmartAsync(new EmailFilter
               { TicketType = TicketType.Reopen, MaxResults = max });

    // =========================================================================
    // 7. PRIORITY QUEUE  — AI-powered work queue for support agents
    // =========================================================================

    /// <summary>
    /// Returns emails sorted by AI-derived priority.
    /// Angry + Critical first, down to informational last.
    /// </summary>
    public async Task<List<AnnotatedEmail>> GetPriorityQueueAsync(int max = 20)
    {
        // Only open-state emails make it into the queue
        // We use keyword heuristics first to limit API calls
        var candidates = _emails
            .Where(e => e.IsUnread ||
                        e.Subject.Contains("urgent", StringComparison.OrdinalIgnoreCase) ||
                        e.Subject.Contains("escalat", StringComparison.OrdinalIgnoreCase) ||
                        e.Subject.Contains("UNACCEPTABLE", StringComparison.OrdinalIgnoreCase) ||
                        e.Subject.Contains("disaster", StringComparison.OrdinalIgnoreCase) ||
                        e.Subject.Contains("reopen", StringComparison.OrdinalIgnoreCase))
            .Take(60)
            .ToList();

        // Supplement with recent emails if we don't have enough
        if (candidates.Count < 30)
        {
            var recent = _emails.OrderByDescending(e => e.Date).Take(40);
            candidates = candidates.Union(recent).Distinct().ToList();
        }

        var annotated = await _analysis.AnalyzeBatchAsync(candidates);

        return annotated
            .Where(a => OpenTypes.Contains(a.Analysis.TicketType))
            .OrderBy(a => EmailAnalysisService.GetPriorityScore(a.Analysis))
            .Take(max)
            .ToList();
    }

    // =========================================================================
    // 8. OPEN / CLOSED TICKETS  (AI-derived status)
    // =========================================================================

    public async Task<List<TicketSummary>> GetOpenTicketsAsync(int maxTickets = 20)
    {
        var ticketNumbers = GetAllTicketNumbers().Take(maxTickets * 2).ToList();
        var summaries = new List<TicketSummary>();

        foreach (var tn in ticketNumbers)
        {
            var summary = await GetTicketSummaryAsync(tn);
            if (summary != null && OpenTypes.Contains(summary.Analysis.CurrentStatus))
                summaries.Add(summary);
            if (summaries.Count >= maxTickets) break;
        }

        return summaries.OrderByDescending(s => s.LatestEmail.Date).ToList();
    }

    public async Task<List<TicketSummary>> GetClosedTicketsAsync(int maxTickets = 20)
    {
        var ticketNumbers = GetAllTicketNumbers().Take(maxTickets * 2).ToList();
        var summaries = new List<TicketSummary>();

        foreach (var tn in ticketNumbers)
        {
            var summary = await GetTicketSummaryAsync(tn);
            if (summary != null && ClosedTypes.Contains(summary.Analysis.CurrentStatus))
                summaries.Add(summary);
            if (summaries.Count >= maxTickets) break;
        }

        return summaries.OrderByDescending(s => s.LatestEmail.Date).ToList();
    }

    // =========================================================================
    // 9. SENDER PROFILE  — combines raw + AI analysis
    // =========================================================================

    public async Task<SenderProfile?> GetSenderProfileAsync(string senderEmail)
    {
        var senderEmails = GetBySender(senderEmail);
        if (senderEmails.Count == 0) return null;

        var annotated = await _analysis.AnalyzeBatchAsync(senderEmails);
        var first     = senderEmails.First();

        var tickets = senderEmails.GroupBy(e => e.TicketNumber).ToList();
        var ticketSummaries = new List<TicketSummary>();
        foreach (var g in tickets)
        {
            var summary = await GetTicketSummaryAsync(g.Key);
            if (summary != null) ticketSummaries.Add(summary);
        }

        var dominantSentiment = annotated
            .GroupBy(a => a.Analysis.Sentiment)
            .OrderByDescending(g => g.Count()).First().Key;

        var dominantUrgency = annotated
            .GroupBy(a => a.Analysis.UrgencyLevel)
            .OrderByDescending(g => g.Count()).First().Key;

        return new SenderProfile
        {
            SenderEmail       = senderEmail,
            SenderName        = first.SenderName,
            Company           = first.Company,
            TotalEmails       = senderEmails.Count,
            LastContact       = senderEmails.Max(e => e.Date),
            OpenTickets       = ticketSummaries.Count(s => OpenTypes.Contains(s.Analysis.CurrentStatus)),
            EscalationCount   = annotated.Count(a => a.Analysis.TicketType == TicketType.Escalation),
            AngryCount        = annotated.Count(a => a.Analysis.TicketType == TicketType.Angry),
            DominantSentiment = dominantSentiment,
            DominantUrgency   = dominantUrgency,
            Products          = annotated.Select(a => a.Analysis.Product)
                                         .Where(p => !string.IsNullOrEmpty(p))
                                         .Distinct().ToList(),
            Tickets           = ticketSummaries
        };
    }

    // =========================================================================
    // 10. INBOX STATS  — dashboard overview (AI-powered)
    // =========================================================================

    public async Task<InboxStats> GetInboxStatsAsync(int sampleSize = 80)
    {
        // Use a sample for stats to limit API calls
        var sample    = _emails.OrderByDescending(e => e.Date).Take(sampleSize).ToList();
        var annotated = await _analysis.AnalyzeBatchAsync(sample);

        // Thread-level status from latest email per ticket
        var ticketStatus = annotated
            .GroupBy(a => a.Email.TicketNumber)
            .Select(g => g.OrderBy(a => a.Email.Date).Last())
            .ToList();

        return new InboxStats
        {
            TotalEmails      = _emails.Count,
            UnreadCount      = _emails.Count(e => e.IsUnread),
            OpenTickets      = ticketStatus.Count(a => OpenTypes.Contains(a.Analysis.TicketType)),
            EscalatedTickets = annotated.Count(a => a.Analysis.TicketType == TicketType.Escalation),
            AngryCount       = annotated.Count(a => a.Analysis.TicketType == TicketType.Angry),
            CriticalCount    = annotated.Count(a => a.Analysis.UrgencyLevel == UrgencyLevel.Critical),
            NeedsEscalation  = annotated.Count(a => a.Analysis.NeedsHumanEscalation),

            ByTicketType = annotated
                .GroupBy(a => a.Analysis.TicketType.ToString())
                .ToDictionary(g => g.Key, g => g.Count()),

            BySentiment = annotated
                .GroupBy(a => a.Analysis.Sentiment.ToString())
                .ToDictionary(g => g.Key, g => g.Count()),

            ByUrgency = annotated
                .GroupBy(a => a.Analysis.UrgencyLevel.ToString())
                .ToDictionary(g => g.Key, g => g.Count()),

            ByProduct = annotated
                .Where(a => !string.IsNullOrWhiteSpace(a.Analysis.Product))
                .GroupBy(a => a.Analysis.Product)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .ToDictionary(g => g.Key, g => g.Count()),

            TopEscalations = new(), // populated via GetOpenTicketsAsync if needed
            RecentAngry    = new()
        };
    }

    // =========================================================================
    // 11. PAGINATION
    // =========================================================================

    public (List<EmailMessage> Items, int TotalCount, int TotalPages) GetPaged(
        EmailFilter filter, int page = 1, int pageSize = 20)
    {
        var pagedFilter = new EmailFilter
        {
            SenderEmail  = filter.SenderEmail,
            Company      = filter.Company,
            TicketNumber = filter.TicketNumber,
            Keyword      = filter.Keyword,
            IsUnread     = filter.IsUnread,
            FromDate     = filter.FromDate,
            ToDate       = filter.ToDate,
            MaxResults   = int.MaxValue
        };
        var all   = FilterRaw(pagedFilter);
        var total = all.Count;
        var pages = (int)Math.Ceiling(total / (double)pageSize);
        var items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return (items, total, pages);
    }

    // =========================================================================
    // 12. CACHE & DIAGNOSTICS
    // =========================================================================

    public (int EmailsCached, int ThreadsCached) GetCacheStats()
        => _analysis.CacheStats();

    public int TotalEmailsLoaded => _emails.Count;
}
