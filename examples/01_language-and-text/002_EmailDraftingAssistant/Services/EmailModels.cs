namespace _002_EmailDraftingAssistant.Models;

// =============================================================================
// AI-DERIVED ANALYSIS  (produced by EmailAnalysisService, never stored in CSV)
// =============================================================================

public enum TicketType
{
    NewTicket, FollowUp, Escalation, Resolution,
    Satisfaction, Complaint, Angry, Closure, Reopen, Billing, Unknown
}

public enum UrgencyLevel  { Low, Medium, High, Critical }
public enum Sentiment     { Positive, Neutral, Frustrated, Angry }
public enum TicketStatus  { Open, Closed }

public class EmailAnalysis
{
    public string       MessageId            { get; set; } = string.Empty;
    public TicketType   TicketType           { get; set; }
    public UrgencyLevel UrgencyLevel         { get; set; }
    public Sentiment    Sentiment            { get; set; }
    public string       Product              { get; set; } = string.Empty;
    public string       Intent               { get; set; } = string.Empty;
    public string       SuggestedTone        { get; set; } = string.Empty;
    public bool         NeedsHumanEscalation { get; set; }
    public string       RawJson              { get; set; } = string.Empty;
}

public class ThreadAnalysis
{
    public string       TicketNumber     { get; set; } = string.Empty;
    public TicketType   CurrentStatus    { get; set; }
    public TicketStatus TicketStatus     { get; set; }
    public UrgencyLevel UrgencyLevel     { get; set; }
    public Sentiment    OverallSentiment { get; set; }
    public string       Product          { get; set; } = string.Empty;
    public string       IssueSummary     { get; set; } = string.Empty;
    public string       SuggestedAction  { get; set; } = string.Empty;
    public bool         NeedsEscalation  { get; set; }
    public int          EmailCount       { get; set; }
    public string       RawJson          { get; set; } = string.Empty;
}

public class AnnotatedEmail
{
    public EmailMessage  Email    { get; set; } = new();
    public EmailAnalysis Analysis { get; set; } = new();
}

public class TicketSummary
{
    public string            TicketNumber { get; set; } = string.Empty;
    public ThreadAnalysis    Analysis     { get; set; } = new();
    public List<EmailMessage> Thread      { get; set; } = new();
    public EmailMessage      LatestEmail  { get; set; } = new();
    public EmailMessage      FirstEmail   { get; set; } = new();
}

public class EmailFilter
{
    public string?      SenderEmail  { get; set; }
    public string?      Company      { get; set; }
    public string?      TicketNumber { get; set; }
    public string?      Keyword      { get; set; }
    public bool?        IsUnread     { get; set; }
    public DateTime?    FromDate     { get; set; }
    public DateTime?    ToDate       { get; set; }
    public TicketType?  TicketType   { get; set; }
    public UrgencyLevel? UrgencyLevel { get; set; }
    public Sentiment?   Sentiment    { get; set; }
    public string?      Product      { get; set; }
    public int          MaxResults   { get; set; } = 50;
}

public class InboxStats
{
    public int TotalEmails      { get; set; }
    public int UnreadCount      { get; set; }
    public int OpenTickets      { get; set; }
    public int EscalatedTickets { get; set; }
    public int AngryCount       { get; set; }
    public int CriticalCount    { get; set; }
    public int NeedsEscalation  { get; set; }
    public Dictionary<string, int> ByTicketType { get; set; } = new();
    public Dictionary<string, int> BySentiment  { get; set; } = new();
    public Dictionary<string, int> ByUrgency    { get; set; } = new();
    public Dictionary<string, int> ByProduct    { get; set; } = new();
    public List<TicketSummary> TopEscalations   { get; set; } = new();
    public List<TicketSummary> RecentAngry      { get; set; } = new();
}

public class SenderProfile
{
    public string       SenderEmail       { get; set; } = string.Empty;
    public string       SenderName        { get; set; } = string.Empty;
    public string       Company           { get; set; } = string.Empty;
    public int          TotalEmails       { get; set; }
    public DateTime     LastContact       { get; set; }
    public int          OpenTickets       { get; set; }
    public int          EscalationCount   { get; set; }
    public int          AngryCount        { get; set; }
    public Sentiment    DominantSentiment { get; set; }
    public UrgencyLevel DominantUrgency   { get; set; }
    public List<string> Products          { get; set; } = new();
    public List<TicketSummary> Tickets    { get; set; } = new();
}
