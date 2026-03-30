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
        }
    }
}
