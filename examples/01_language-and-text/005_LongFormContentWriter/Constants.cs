namespace _005_LongFormContentWriter
{
    public static class Constants
    {
        public static class Prompts
        {
            // ── Outline stage ────────────────────────────────────────────────────

            public const string OutlineSystemPrompt =
                "You are an expert SEO content strategist with deep knowledge of search intent, " +
                "heading hierarchy, and content architecture. Always respond with valid JSON only.";

            public const string OutlinePrompt = @"You are planning a long-form article for the following brief.

Primary keyword: {{primary_keyword}}
Secondary keywords: {{secondary_keywords}}
Target audience: {{target_audience}}
Search intent: {{search_intent}}
Tone: {{tone}}
Target word count: {{target_word_count}}
Call to action: {{call_to_action}}

Competitor research (top-ranking pages for this keyword):
{{competitor_research}}

Your task:
1. Infer the dominant search intent from the competitor evidence.
2. Propose an SEO-optimised H1 title that places the primary keyword naturally.
3. Design a logical H2/H3 reading flow that covers the topic thoroughly and matches intent.
4. Identify FAQ opportunities that competitors miss or underserve.
5. Suggest a short, readable URL slug.

Respond ONLY with valid JSON (no markdown, no code block):
{""seo_title"": ""full article title with keyword"", ""slug"": ""url-slug-here"", ""outline_sections"": [""H2: Section title"", ""H3: Sub-section title"", ""H2: Next section""], ""faq_ideas"": [""Question one?"", ""Question two?"", ""Question three?""]}";

            // ── Draft stage ──────────────────────────────────────────────────────

            public const string DraftSystemPrompt =
                "You are an expert long-form content writer specialising in SEO-optimised articles. " +
                "You write engaging, well-structured content that satisfies search intent, avoids " +
                "keyword stuffing, and earns reader trust through clarity and depth.";

            public const string DraftPrompt = @"Write a complete long-form article using the plan below.

SEO title: {{seo_title}}
Primary keyword: {{primary_keyword}}
Secondary keywords: {{secondary_keywords}}
Target audience: {{target_audience}}
Tone: {{tone}}
Target word count: {{target_word_count}}
Call to action: {{call_to_action}}

Approved outline (follow this structure exactly):
{{outline_sections_text}}

Competitor research context (use for grounding, do not copy):
{{competitor_research}}

Writing guidelines:
- Open with a strong hook that addresses the reader's problem directly.
- Follow the approved outline structure using proper Markdown headings (## for H2, ### for H3).
- Integrate the primary keyword naturally in the title, first paragraph, and 2-3 section headings.
- Weave secondary keywords in where they fit the context — never force them.
- Keep paragraphs short (3-5 sentences) for scannability.
- Use bullet lists or numbered steps where they improve clarity.
- Avoid invented statistics or unverifiable claims.
- Close with a clear call to action: {{call_to_action}}

Write the full article now in Markdown:";

            // ── Metadata stage ───────────────────────────────────────────────────

            public const string MetaSystemPrompt =
                "You are an expert SEO metadata specialist. You craft concise, click-worthy " +
                "meta descriptions and title variants that improve organic CTR. Always respond with valid JSON only.";

            public const string MetaPrompt = @"Generate SEO metadata for the article below.

Primary keyword: {{primary_keyword}}
Article title: {{seo_title}}
Target audience: {{target_audience}}

Article opening (first 800 characters):
{{article_preview}}

Requirements:
- Meta description: 145-160 characters, includes primary keyword, clear value proposition, soft CTA.
- Title variants: 3 alternate SEO titles with different angles (question, list, how-to) but same keyword.
- Social preview: 1-2 punchy sentences for social sharing (under 200 characters).

Respond ONLY with valid JSON (no markdown, no code block):
{""meta_description"": ""...(145-160 chars)"", ""title_variants"": [""Variant 1"", ""Variant 2"", ""Variant 3""], ""social_preview"": ""...""}";
        }

        public static class Messages
        {
            public const string EmptyKeyword = "Primary keyword cannot be empty.";
            public const string KeywordTooLong = "Primary keyword must not exceed 200 characters.";
            public const string OpenAiKeyNotConfigured = "OpenAI API key is not configured.";
            public const string SearchApiKeyNotConfigured = "Search API key is not configured.";
            public const string WorkflowFailed = "Content generation failed. Please try again.";
            public const string UnexpectedError = "An unexpected error occurred.";
        }

        public static class Tones
        {
            public const string Expert = "expert and authoritative";
            public const string Conversational = "friendly and conversational";
            public const string Practical = "practical and actionable";
            public const string Formal = "formal and professional";
        }
    }
}
