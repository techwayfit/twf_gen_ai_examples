using System.Globalization;
using _002_EmailDraftingAssistant.Models;

namespace _002_EmailDraftingAssistant.Services;

/// <summary>
/// Reads techwayfit_emails_raw.csv (raw fields only — no AI-derived columns).
/// Full RFC-4180 parser handles quoted fields with embedded newlines.
/// </summary>
public class CsvEmailReader
{
    private readonly string _filePath;

    public CsvEmailReader(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"CSV not found: {filePath}");
        _filePath = filePath;
    }

    public List<EmailMessage> ReadAll()
    {
        var emails  = new List<EmailMessage>();
        var rows    = ParseRfc4180(_filePath);
        if (rows.Count < 2) return emails;

        var headers = rows[0];
        var idx     = headers.Select((h, i) => (h.Trim(), i))
                             .ToDictionary(t => t.Item1, t => t.Item2);

        for (int i = 1; i < rows.Count; i++)
        {
            var c = rows[i];
            if (c.Count < headers.Count) continue;
            try
            {
                emails.Add(new EmailMessage
                {
                    MessageId    = Get(c, idx, "MessageId"),
                    ThreadId     = Get(c, idx, "ThreadId"),
                    TicketNumber = Get(c, idx, "TicketNumber"),
                    Date         = ParseDate(Get(c, idx, "Date")),
                    From         = Get(c, idx, "From"),
                    SenderName   = Get(c, idx, "SenderName"),
                    SenderEmail  = Get(c, idx, "SenderEmail"),
                    Company      = Get(c, idx, "Company"),
                    Subject      = Get(c, idx, "Subject"),
                    Body         = Get(c, idx, "Body"),
                    IsUnread     = Get(c, idx, "IsUnread").Equals("True", StringComparison.OrdinalIgnoreCase),
                    Labels       = Get(c, idx, "Labels")
                                       .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                       .Select(l => l.Trim()).ToList()
                });
            }
            catch { /* skip malformed rows */ }
        }
        return emails;
    }

    private static string Get(List<string> cols, Dictionary<string, int> idx, string name)
        => idx.TryGetValue(name, out var i) && i < cols.Count ? cols[i] : string.Empty;

    private static DateTime ParseDate(string raw)
        => DateTime.TryParseExact(raw, "yyyy-MM-dd HH:mm:ss",
               CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
           ? dt : DateTime.MinValue;

    private static List<List<string>> ParseRfc4180(string path)
    {
        var rows    = new List<List<string>>();
        var current = new List<string>();
        var field   = new System.Text.StringBuilder();
        bool inQuote = false;

        using var reader = new StreamReader(path, System.Text.Encoding.UTF8);
        int ch;
        while ((ch = reader.Read()) != -1)
        {
            char c = (char)ch;
            if (inQuote)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"') { reader.Read(); field.Append('"'); }
                    else inQuote = false;
                }
                else field.Append(c);
            }
            else
            {
                switch (c)
                {
                    case '"':  inQuote = true; break;
                    case ',':  current.Add(field.ToString()); field.Clear(); break;
                    case '\n': current.Add(field.ToString()); field.Clear();
                               rows.Add(current); current = new List<string>(); break;
                    case '\r': break;
                    default:   field.Append(c); break;
                }
            }
        }
        if (field.Length > 0 || current.Count > 0)
        { current.Add(field.ToString()); rows.Add(current); }

        return rows;
    }
}
