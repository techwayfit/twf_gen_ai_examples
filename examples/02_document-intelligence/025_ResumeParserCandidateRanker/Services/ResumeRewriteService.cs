using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace _025_ResumeParserCandidateRanker.Services;

/// <summary>
/// Calls the LLM to produce a job-targeted version of a resume and renders it
/// as a self-contained, print-ready HTML document.
/// </summary>
public class ResumeRewriteService(LlmService llmService, ILogger<ResumeRewriteService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Rewrites <paramref name="resumeText"/> to target <paramref name="jobDescription"/>.
    /// Returns a complete, self-contained HTML string ready for browser preview and printing.
    /// </summary>
    public async Task<string> RewriteAsync(
        string            jobDescription,
        string            resumeText,
        string            apiKey,
        string            model    = "gpt-4o-mini",
        string            endpoint = "https://api.openai.com/v1/chat/completions",
        CancellationToken ct       = default)
    {
        var prompt = Constants.Prompts.ResumeRewritePrompt
            .Replace("{{job_description}}", jobDescription.Length > 5_000
                ? jobDescription[..5_000] : jobDescription)
            .Replace("{{resume_text}}", resumeText.Length > 12_000
                ? resumeText[..12_000] : resumeText);

        logger.LogInformation("Requesting tailored resume rewrite from LLM");

        var json = await llmService.CompleteAsync(
            Constants.Prompts.ResumeRewriteSystemPrompt,
            prompt,
            apiKey,
            model,
            endpoint,
            maxTokens: 2500,
            ct);

        json = StripCodeFences(json);

        TailoredResumeDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<TailoredResumeDto>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"LLM returned invalid JSON for tailored resume: {ex.Message}", ex);
        }

        if (dto is null) throw new InvalidOperationException("LLM returned an empty tailored resume.");

        logger.LogInformation("Tailored resume generated for '{Name}'", dto.Name);
        return RenderHtml(dto);
    }

    // ── HTML renderer ─────────────────────────────────────────────────────────

    private static string RenderHtml(TailoredResumeDto r)
    {
        var sb   = new StringBuilder(8_192);
        var name = E(r.Name ?? "Resume");
        var c    = r.Contact;

        var contactParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(c?.Email))
            contactParts.Add($"<a href=\"mailto:{E(c.Email)}\">{E(c.Email)}</a>");
        if (!string.IsNullOrWhiteSpace(c?.Phone))
            contactParts.Add(E(c.Phone));
        if (!string.IsNullOrWhiteSpace(c?.Location))
            contactParts.Add(E(c.Location));
        if (!string.IsNullOrWhiteSpace(c?.Linkedin))
            contactParts.Add($"<a href=\"{E(c.Linkedin)}\" target=\"_blank\">{E(c.Linkedin)}</a>");

        // ── Document head + styles ────────────────────────────────────────────
        sb.Append($$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>{{name}} — Resume</title>
<style>
*, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
body {
  font-family: 'Segoe UI', system-ui, -apple-system, Arial, sans-serif;
  font-size: 11pt;
  color: #1a1a1a;
  background: #eef2f7;
  padding: 2rem;
}
.resume {
  max-width: 820px;
  margin: 0 auto;
  background: #fff;
  box-shadow: 0 2px 20px rgba(0,0,0,.12);
  border-radius: 6px;
  overflow: hidden;
}
.resume-header {
  background: #1a56db;
  color: #fff;
  padding: 2rem 2.5rem 1.6rem;
}
.resume-header h1 {
  font-size: 2rem;
  font-weight: 700;
  letter-spacing: -.5px;
  line-height: 1.2;
  margin-bottom: .25rem;
}
.headline {
  font-size: 1.05rem;
  opacity: .9;
  margin-bottom: .8rem;
  font-style: italic;
}
.contact-line {
  font-size: .86rem;
  opacity: .85;
  display: flex;
  flex-wrap: wrap;
  gap: .35rem 1.25rem;
  align-items: center;
}
.contact-line a { color: #cfe2ff; text-decoration: none; }
.contact-line a:hover { text-decoration: underline; }
.resume-body { padding: 1.75rem 2.5rem 2rem; }
.section { margin-bottom: 1.6rem; }
.section-title {
  font-size: .68rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 1.8px;
  color: #1a56db;
  border-bottom: 2px solid #1a56db;
  padding-bottom: .3rem;
  margin-bottom: .9rem;
}
.summary-text { line-height: 1.7; color: #333; }
.skills-grid { display: flex; flex-wrap: wrap; gap: .4rem; }
.skill-tag {
  background: #e8f0fe;
  color: #1a56db;
  border-radius: 4px;
  padding: .22em .65em;
  font-size: .82rem;
  font-weight: 500;
  white-space: nowrap;
}
.exp-entry { margin-bottom: 1.1rem; padding-bottom: 1.1rem; border-bottom: 1px solid #f0f0f0; }
.exp-entry:last-child { border-bottom: none; margin-bottom: 0; padding-bottom: 0; }
.exp-header {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  flex-wrap: wrap;
  gap: .2rem;
  margin-bottom: .35rem;
}
.exp-title { font-weight: 700; font-size: 1rem; }
.exp-company { color: #444; font-size: .92rem; }
.exp-duration { font-size: .84rem; color: #666; white-space: nowrap; }
.exp-bullets { margin-top: .4rem; padding-left: 1.2rem; }
.exp-bullets li { margin-bottom: .3rem; line-height: 1.55; color: #333; font-size: .91rem; }
.edu-entry { display: flex; flex-wrap: wrap; justify-content: space-between; margin-bottom: .5rem; }
.edu-left { line-height: 1.5; }
.edu-degree { font-weight: 700; }
.edu-inst { color: #444; }
.edu-year { font-size: .86rem; color: #666; }
.cert-list { padding-left: 1.2rem; }
.cert-list li { margin-bottom: .3rem; font-size: .91rem; color: #333; line-height: 1.5; }
@media print {
  body { background: none; padding: 0; font-size: 10pt; }
  .resume { box-shadow: none; border-radius: 0; max-width: 100%; }
  .resume-header { background: #1a56db !important; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
  .skill-tag { background: #e8f0fe !important; -webkit-print-color-adjust: exact; print-color-adjust: exact; border: 1px solid #1a56db; }
  .exp-entry { page-break-inside: avoid; }
  a { color: inherit; text-decoration: none; }
  .contact-line a { color: #cfe2ff; }
}
</style>
</head>
<body>
<div class="resume">
  <header class="resume-header">
    <h1>{{name}}</h1>
""");

        if (!string.IsNullOrWhiteSpace(r.Headline))
            sb.AppendLine($"""    <div class="headline">{E(r.Headline)}</div>""");

        if (contactParts.Count > 0)
            sb.AppendLine($"""    <div class="contact-line">{string.Join(" <span>·</span> ", contactParts)}</div>""");

        sb.AppendLine("  </header>");
        sb.AppendLine("""  <div class="resume-body">""");

        // ── Summary ──────────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(r.Summary))
        {
            sb.AppendLine("""    <section class="section">""");
            sb.AppendLine("""      <div class="section-title">Professional Summary</div>""");
            sb.AppendLine($"""      <p class="summary-text">{E(r.Summary)}</p>""");
            sb.AppendLine("    </section>");
        }

        // ── Skills ───────────────────────────────────────────────────────────
        if (r.Skills is { Count: > 0 })
        {
            sb.AppendLine("""    <section class="section">""");
            sb.AppendLine("""      <div class="section-title">Skills</div>""");
            sb.AppendLine("""      <div class="skills-grid">""");
            foreach (var skill in r.Skills)
                sb.AppendLine($"""        <span class="skill-tag">{E(skill)}</span>""");
            sb.AppendLine("      </div>");
            sb.AppendLine("    </section>");
        }

        // ── Experience ────────────────────────────────────────────────────────
        if (r.Experience is { Count: > 0 })
        {
            sb.AppendLine("""    <section class="section">""");
            sb.AppendLine("""      <div class="section-title">Experience</div>""");
            foreach (var exp in r.Experience)
            {
                sb.AppendLine("""      <div class="exp-entry">""");
                sb.AppendLine("""        <div class="exp-header">""");
                sb.AppendLine($"""          <div><span class="exp-title">{E(exp.Title)}</span>&nbsp;·&nbsp;<span class="exp-company">{E(exp.Company)}</span></div>""");
                if (!string.IsNullOrWhiteSpace(exp.Duration))
                    sb.AppendLine($"""          <span class="exp-duration">{E(exp.Duration)}</span>""");
                sb.AppendLine("        </div>");
                if (exp.Bullets is { Count: > 0 })
                {
                    sb.AppendLine("""        <ul class="exp-bullets">""");
                    foreach (var b in exp.Bullets)
                        sb.AppendLine($"          <li>{E(b)}</li>");
                    sb.AppendLine("        </ul>");
                }
                sb.AppendLine("      </div>");
            }
            sb.AppendLine("    </section>");
        }

        // ── Education ─────────────────────────────────────────────────────────
        if (r.Education is { Count: > 0 })
        {
            sb.AppendLine("""    <section class="section">""");
            sb.AppendLine("""      <div class="section-title">Education</div>""");
            foreach (var edu in r.Education)
            {
                sb.AppendLine("""      <div class="edu-entry">""");
                sb.AppendLine($"""        <div class="edu-left"><span class="edu-degree">{E(edu.Degree)}</span> — <span class="edu-inst">{E(edu.Institution)}</span></div>""");
                if (!string.IsNullOrWhiteSpace(edu.Year))
                    sb.AppendLine($"""        <span class="edu-year">{E(edu.Year)}</span>""");
                sb.AppendLine("      </div>");
            }
            sb.AppendLine("    </section>");
        }

        // ── Certifications ────────────────────────────────────────────────────
        if (r.Certifications is { Count: > 0 })
        {
            sb.AppendLine("""    <section class="section">""");
            sb.AppendLine("""      <div class="section-title">Certifications</div>""");
            sb.AppendLine("""      <ul class="cert-list">""");
            foreach (var cert in r.Certifications)
                sb.AppendLine($"        <li>{E(cert)}</li>");
            sb.AppendLine("      </ul>");
            sb.AppendLine("    </section>");
        }

        sb.AppendLine("  </div>"); // resume-body
        sb.AppendLine("</div>"); // resume
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

    private static string StripCodeFences(string s)
    {
        s = s.Trim();
        if (s.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) s = s[7..];
        else if (s.StartsWith("```")) s = s[3..];
        if (s.EndsWith("```")) s = s[..^3];
        return s.Trim();
    }
}

// ── DTOs (internal to this assembly) ────────────────────────────────────────

sealed class TailoredResumeDto
{
    public string?               Name           { get; set; }
    public string?               Headline       { get; set; }
    public TailoredContactDto?   Contact        { get; set; }
    public string?               Summary        { get; set; }
    public List<string>?         Skills         { get; set; }
    public List<TailoredExpDto>? Experience     { get; set; }
    public List<TailoredEduDto>? Education      { get; set; }
    public List<string>?         Certifications { get; set; }
}

sealed class TailoredContactDto
{
    public string? Email    { get; set; }
    public string? Phone    { get; set; }
    public string? Location { get; set; }
    public string? Linkedin { get; set; }
}

sealed class TailoredExpDto
{
    public string?       Title    { get; set; }
    public string?       Company  { get; set; }
    public string?       Duration { get; set; }
    public List<string>? Bullets  { get; set; }
}

sealed class TailoredEduDto
{
    public string? Degree      { get; set; }
    public string? Institution { get; set; }
    public string? Year        { get; set; }
}
