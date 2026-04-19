namespace _000_LinkedInPostGenerator
{
    public static class Constants
    {
        public static class Prompts
        {
            // ── System prompt — built from author profile ─────────────────────────
            // The {0}, {1}, {2} placeholders are filled by ProfileService.BuildSystemPrompt()

            public const string SystemPromptTemplate =
                "You are a LinkedIn post writer for the following author.\n\n" +
                "AUTHOR BIO:\n{0}\n\n" +
                "WRITING GUIDELINES:\n{1}\n\n" +
                "PREVIOUS POSTS (use these to anchor tone and style):\n{2}\n\n" +
                "Always write posts that feel personal and specific to this author's voice. " +
                "Never sound generic or corporate.";

            // ── Post generation prompt ─────────────────────────────────────────────

            public const string GenerationSystemPrompt =
                "You are an expert LinkedIn post writer. You craft engaging, authentic posts " +
                "that drive real professional conversations. Always respond with only the post " +
                "text — no preamble, no explanation, no quotation marks around the post.";

            public const string GenerationPrompt = @"Write an engaging LinkedIn post for the author described in the system prompt.

TOPIC: {{topic}}
AUTHOR'S CURRENT ROLE: {{current_role}}
TARGET AUDIENCE: {{target_audience}}
TARGET LENGTH: Approximately {{max_chars}} characters
{{related_links_section}}
{{additional_context_section}}

REQUIREMENTS:
- Open with a compelling hook (bold statement, surprising fact, or direct question)
- Use short paragraphs of 1–3 lines for mobile readability
- Write in the author's voice as described in the system prompt
- Be specific and concrete — avoid vague generalisations
- End with a clear call to action or thought-provoking question
- Do NOT include hashtags (added separately)
- Target approximately {{max_chars}} characters

FORMATTING RULES (critical — LinkedIn is plain text):
- Do NOT use markdown syntax. No **bold**, no *italic*, no # headings, no bullet hyphens preceded by *.
- For emphasis, use plain text phrasing or Unicode emojis as section markers (e.g. 🔹 🔸 ✅ ⚡).
- Use a blank line between paragraphs.
- Bullet points may use — or 🔹 as a prefix, never *.

Return only the post text — no preamble, no explanation.";

            // ── Length adjustment prompt ───────────────────────────────────────────

            public const string AdjustmentSystemPrompt =
                "You are a LinkedIn post editor. Your job is to adjust a post to match a " +
                "target character count while preserving the hook, core message, and call to action. " +
                "Return only the adjusted post text — nothing else.";

            public const string AdjustmentPrompt = @"Here is a LinkedIn post:

{{raw_post}}

Current length: {{raw_post_length}} characters
Target length: {{max_chars}} characters

{{adjustment_instruction}}

Return only the adjusted post text.";
        }

        public static class Messages
        {
            public const string EmptyTopic          = "Topic cannot be empty.";
            public const string EmptyRole           = "Current role cannot be empty.";
            public const string TopicTooLong        = "Topic must not exceed 2 000 characters.";
            public const string OpenAiKeyNotConfigured = "OpenAI API key is not configured. Add your key to appsettings.local.json.";
            public const string WorkflowFailed      = "Post generation failed. Please try again.";
            public const string UnexpectedError     = "An unexpected error occurred.";
        }

        public static class Roles
        {
            public static readonly string[] All =
            {
                "developer",
                "senior-developer",
                "tech-lead",
                "architect",
                "engineering-manager",
                "vp-engineering",
                "cto",
                "product-manager",
                "designer",
                "data-scientist",
                "devops-engineer",
                "consultant"
            };
        }

        public static class Audiences
        {
            public static readonly string[] All =
            {
                "engineers",
                "developers",
                "tech-leads",
                "architects",
                "engineering-managers",
                "product-managers",
                "executives",
                "hiring-managers",
                "job-seekers",
                "startup-founders",
                "general-professionals"
            };
        }
    }
}
