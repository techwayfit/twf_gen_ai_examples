namespace _036_AI_Pair_Programmer;

public static class Constants
{
    public static class Prompts
    {
        public const string PairProgrammerSystemPrompt =
            "You are a senior software engineer acting as an AI pair programmer. " +
            "Use retrieved code context to answer precisely, preserve architecture conventions, " +
            "and avoid speculative changes that are not grounded in evidence.";

        public const string PairProgrammerPrompt =
            "Task type: {{task_type}}\n" +
            "Developer request:\n{{user_request}}\n\n" +
            "Repository context (highest relevance first):\n{{retrieved_context}}\n\n" +
            "Return ONLY valid JSON with this shape:\n" +
            "{\n" +
            "  \"summary\": \"string\",\n" +
            "  \"summary_markdown\": \"markdown string\",\n" +
            "  \"implementation_plan\": [\"step\"],\n" +
            "  \"files_to_change\": [\"path\"],\n" +
            "  \"code_blocks\": [{ \"file\": \"path\", \"language\": \"text\", \"content\": \"code\" }],\n" +
            "  \"risks\": [\"risk\"]\n" +
            "}\n" +
            "If context is insufficient, say so in summary and risks, and still provide the safest plan.";
    }

    public static class Messages
    {
        public const string OpenAiKeyNotConfigured = "OpenAI API key is not configured. Add it to appsettings.local.json.";
        public const string RepoPathRequired = "Repository path is required.";
        public const string RepoPathNotFound = "Repository path does not exist.";
        public const string UserRequestRequired = "User request cannot be empty.";
        public const string IndexNotFound = "No index found for this repository. Run indexing first.";
    }
}
