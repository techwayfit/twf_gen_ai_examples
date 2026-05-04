namespace _021_MultiDocumentResearchSynthesizer;

public static class Constants
{
    public static class Prompts
    {
        // ── Synthesis stage ───────────────────────────────────────────────────

        public const string SynthesisSystemPrompt =
            "You are a research synthesis assistant. Answer the user's question using ONLY the " +
            "numbered context blocks provided below. For every claim you make, cite the source " +
            "using [source_N] notation corresponding to the context block number. " +
            "If the context does not contain sufficient information to answer the question, " +
            "state that explicitly. Do not speculate or use outside knowledge. " +
            "Format the answer field using Markdown: use ## headings to organise sections, " +
            "**bold** for key terms, bullet lists for enumerated points, and keep paragraphs short. " +
            "Always respond with valid JSON only.";

        public const string SynthesisPrompt = @"Context blocks retrieved from the research corpus:

{{retrieved_context}}

Research question: {{question}}

Provide a comprehensive synthesized answer formatted in Markdown (headings, bold, bullets).
After the answer, output a JSON object with:
- ""answer"": your Markdown-formatted synthesized answer with inline [source_N] citations
- ""citations"": array of objects, each with: source_id (e.g. ""source_1""), paper_id, title, authors, year, page, excerpt

Respond ONLY with valid JSON (no outer markdown fences):
{""answer"": ""## Overview\n\nYour **synthesized** answer with [source_1] citations...\n\n## Key Findings\n\n- Point 1 [source_2]\n- Point 2 [source_3]"", ""citations"": [{""source_id"": ""source_1"", ""paper_id"": ""doc_id"", ""title"": ""Paper Title"", ""authors"": [""Author Name""], ""year"": 2024, ""page"": 0, ""excerpt"": ""relevant excerpt from context""}]}";

        // ── Contradiction detection stage ─────────────────────────────────────

        public const string ContradictionSystemPrompt =
            "You are a critical research analyst. Given a synthesized answer and its citations, " +
            "identify any claims where the cited sources contradict or significantly disagree " +
            "with each other. Be specific and precise. Always respond with valid JSON only.";

        public const string ContradictionPrompt = @"Synthesized answer:
{{answer}}

Citations used:
{{citations_text}}

Identify any contradictions or significant disagreements between the cited sources.
For each contradiction, provide: claim (what the disagreement is about), source_a (id of first source), source_b (id of second source), summary (brief explanation of the disagreement).

Respond ONLY with valid JSON (no markdown, no code fences):
{""contradictions"": [{""claim"": ""description of disagreement"", ""source_a"": ""source_1"", ""source_b"": ""source_2"", ""summary"": ""Source 1 states X while Source 2 states Y""}]}";
    }

    public static class Messages
    {
        public const string EmptyQuestion         = "Research question cannot be empty.";
        public const string QuestionTooLong       = "Research question must not exceed 2,000 characters.";
        public const string NoDocumentsIndexed    = "No documents have been indexed yet. Please ingest documents first.";
        public const string EmptyDocumentText     = "Document text cannot be empty.";
        public static string DocumentTooLong(int maxChars) => $"Document text must not exceed {maxChars:N0} characters.";
        public const string OpenAiKeyNotConfigured = "OpenAI API key is not configured. Add it to appsettings.local.json.";
        public const string WorkflowFailed        = "Research synthesis failed. Please try again.";
        public const string IngestFailed          = "Document ingest failed. Please try again.";
        public const string UnexpectedError       = "An unexpected error occurred.";
    }
}
