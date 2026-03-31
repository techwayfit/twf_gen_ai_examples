namespace _002_EmailDraftingAssistant
{
    public static class Constants
    {
        public const string AppName = "Email Drafting Assistant";

        public static class Messages
        {
            public const string EmptyPrompt = "Prompt cannot be empty.";
            public const string OpenApiKeyNotConfigured = "OpenAI API key is not configured.";
            public const string RequestCouldNotProcessed = "The request could not be processed.";
            public const string FailedToProcessRequest = "Failed to process the request.";
        }

        public static class Prompts
        {
            public const string EmailDraftSystemPrompt = "You are a professional email writing assistant. Compose clear, concise, and well-structured emails.";
            public const string EmailDraftPrompt = "Draft a professional email based on the following details:\n\nContext: {{context}}\nTone: {{tone}}\nRecipient: {{recipient}}\n\nWrite only the email body. Use appropriate subject, greeting, body paragraphs, and sign-off.";

            // ── Sentiment Analysis ────────────────────────────────────────────────
            public const string SentimentAnalysisSystemPrompt =
                "You are a customer email sentiment analyzer for TechWayFit, a fitness technology company. " +
                "Analyze customer emails and return structured JSON only. " +
                "Return only valid JSON with no markdown fences, no extra text.";

            public const string SentimentAnalysisPrompt =
                "Analyze the sentiment and intent of this customer email:\n\n" +
                "{{email_body}}\n\n" +
                "Return exactly this JSON structure (no other text):\n" +
                "{\"sentiment\": \"<positive|neutral|frustrated|angry>\", " +
                "\"urgency\": \"<low|medium|high|critical>\", " +
                "\"key_issues\": \"<one-sentence summary of the customer's main concerns>\"}";

            // ── Thread Analysis ───────────────────────────────────────────────────
            public const string ThreadAnalysisSystemPrompt =
                "You are a customer support context analyzer for TechWayFit, a fitness technology company. " +
                "Analyze email thread history to extract what has already been discussed and what remains unresolved. " +
                "Return only valid JSON with no markdown fences, no extra text.";

            public const string ThreadAnalysisPrompt =
                "Analyze the following email thread history and provide a structured summary:\n\n" +
                "{{thread_messages}}\n\n" +
                "Return exactly this JSON structure (no other text):\n" +
                "{\"thread_summary\": \"<2-3 sentence summary of the conversation so far>\", " +
                "\"ongoing_issues\": \"<unresolved issues still needing attention, or 'None'>\"}";

            // ── Final Reply Draft ─────────────────────────────────────────────────
            public const string ReplyDraftSystemPrompt =
                "You are a professional customer support agent for TechWayFit, a fitness technology company. " +
                "Your task is to draft a helpful, empathetic, and appropriately-toned reply to a customer email. " +
                "Write only the email reply body — start from the greeting and end at the sign-off. " +
                "Do not include subject lines, metadata, or explanations outside the email body.";

            public const string ReplyDraftUserPrompt =
                "Please draft a reply to the following customer email.\n\n" +
                "=== ORIGINAL EMAIL ===\n" +
                "From: {senderName} <{senderEmail}>\n" +
                "Subject: {subject}\n" +
                "Date: {date}\n\n" +
                "{body}\n\n" +
                "=== SENTIMENT ANALYSIS ===\n" +
                "Customer Sentiment: {sentiment}\n" +
                "Urgency Level: {urgency}\n" +
                "Key Issues Identified: {keyIssues}\n\n" +
                "=== THREAD CONTEXT ===\n" +
                "Thread Summary: {threadSummary}\n" +
                "Ongoing Unresolved Issues: {ongoingIssues}\n\n" +
                "{senderHistory}" +
                "=== INSTRUCTIONS ===\n" +
                "- Requested tone: {tone}\n" +
                "- Address the customer by their first name\n" +
                "- Directly address every key issue identified: {keyIssues}\n" +
                "- Treat this as {urgency} priority\n" +
                "- Calibrate warmth and empathy to match sentiment: {sentiment}\n" +
                "- Do not repeat what has already been resolved in the thread\n" +
                "- Sign off as: TechWayFit Support Team\n\n" +
                "Write the reply now:";
        }
    }
}
