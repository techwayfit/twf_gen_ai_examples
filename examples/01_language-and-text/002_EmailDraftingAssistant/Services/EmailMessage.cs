namespace _002_EmailDraftingAssistant.Models;

/// <summary>
/// Raw email exactly as stored in CSV / returned by Gmail API.
/// No AI-derived fields — those live in EmailAnalysis.
/// </summary>
public class EmailMessage
{
    public string       MessageId    { get; set; } = string.Empty;
    public string       ThreadId     { get; set; } = string.Empty;
    public string       TicketNumber { get; set; } = string.Empty;
    public DateTime     Date         { get; set; }
    public string       From         { get; set; } = string.Empty;
    public string       SenderName   { get; set; } = string.Empty;
    public string       SenderEmail  { get; set; } = string.Empty;
    public string       Company      { get; set; } = string.Empty;
    public string       Subject      { get; set; } = string.Empty;
    public string       Body         { get; set; } = string.Empty;
    public bool         IsUnread     { get; set; }
    public List<string> Labels       { get; set; } = new();
}
