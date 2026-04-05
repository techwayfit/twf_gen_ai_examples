namespace _003_Automated_NewsLetter;

public static class Constants
{
    public const string AppName     = "TechWayFit Automated Newsletter";
    public const string CompanyName = "TechWayFit Inc.";

    public static class Prompts
    {
        // ── Interest-based relevance filtering ───────────────────────────────

        public const string ClusteringSystemPrompt =
            "You are a news curation assistant. Always respond with valid JSON only — no markdown, no preamble.";

        public const string ClusteringUserPrompt =
            @"Select ONLY the articles relevant to the subscriber's interests and group them by topic.

Subscriber Interests (weight 1–10, higher = more important):
{{interests_text}}

Articles ({{article_count_str}} total):
{{articles_text}}

Return ONLY a JSON array of the relevant articles. Omit articles that do not match any interest. Return only top 5 articles per cluster.
[
  {
    ""url"": ""<original article url>"",
    ""title"": ""<article title>"",
    ""cluster"": ""<topic label — use the subscriber's interest name where possible>""
  }
]

Rules:
- Include only articles that are genuinely relevant to at least one subscriber interest
- Prefer interest names as cluster labels to keep sections aligned with what the subscriber cares about
- Return ONLY the JSON array with no extra text";

        // ── Cluster summarisation ──────────────────────────────────────────────

        public const string ClusterSummarySystemPrompt =
            "You are an expert technology journalist. Always respond with valid JSON only — no markdown, no preamble.";

        public const string ClusterSummaryUserPrompt =
            @"Summarise these articles from the ""{{cluster_label}}"" topic cluster.

Articles:
{{cluster_articles_text}}

Return ONLY a JSON object with this exact structure:
{
  ""label"": ""{{cluster_label}}"",
  ""summary"": ""<2–3 sentence synthesis across all sources — do not summarise any single article>"",
  ""keyTakeaways"": [""<takeaway 1>"", ""<takeaway 2>"", ""<takeaway 3>""],
  ""topArticleUrl"": ""<url of the most important article>"",
  ""imageUrl"": ""<image url from the top article, or empty string>""
}";

        // ── Newsletter generation ──────────────────────────────────────────────

        public const string NewsletterSystemPrompt =
            "You are a knowledgeable technology journalist writing for a busy professional. " +
            "Write in an engaging, {tone} tone. Use clear section headers, smooth transitions between topics, " +
            "and a warm personalised introduction and closing. Format the newsletter in Markdown.";

        public const string NewsletterUserPrompt =
            @"Write a personalised weekly newsletter for {subscriber_name}.

Date range: {date_range}
Preferred tone: {tone}
Number of sections: {section_count}

--- SECTION CONTENT ---
{sections_content}
--- END SECTION CONTENT ---

Write the complete newsletter with:
1. A personalised greeting addressed to {subscriber_name} with a brief 2–3 sentence intro
2. Each section with a ## header, the synthesis paragraph, and key takeaways as bullet points
3. Links to the top articles for each section
4. A brief, warm closing note

Format everything in Markdown.";

        // ── Error / fallback messages ─────────────────────────────────────────

        public static class Messages
        {
            public const string OpenApiKeyNotConfigured = "OpenAI API key is not configured.";
            public const string NoArticlesFetched       = "No articles fetched — check feed configuration.";
            public const string PipelineFailed          = "Newsletter generation failed. Please try again.";
        }
    }
}
