namespace _006_BrandVoiceConsistencyChecker
{
    public static class Constants
    {
        public static class Prompts
        {
            // ── Analysis stage ────────────────────────────────────────────────

            public const string AnalysisSystemPrompt =
                "You are a brand voice specialist. Analyse marketing copy against brand guidelines " +
                "and identify specific deviations in tone, vocabulary, formality, and style. " +
                "Always respond with valid JSON only.";

            public const string AnalysisPrompt = @"Analyse the marketing copy below against the provided brand guidelines and identify any deviations.

{{copy_type}} copy to review:
{{copy_text}}

Brand Guidelines:
{{brand_guidelines}}

Brand: {{brand_name}}
Analysis strictness: {{strictness}}
Semantic similarity score: {{similarity_score}} (0.0 = completely different, 1.0 = identical)

Rules:
- Identify specific excerpts that deviate from the approved brand voice
- Assign a severity level: ""low"", ""medium"", or ""high""
- Categorise each deviation: ""tone"", ""vocabulary"", ""formality"", or ""style""
- Apply {{strictness}} sensitivity — in strict mode flag even subtle deviations
- Return at most 10 violations; omit sections that are fully compliant
- If no violations are found return an empty violations array

Respond ONLY with valid JSON (no markdown, no code fences):
{""violations"": [{""category"": ""tone"", ""severity"": ""high"", ""excerpt"": ""exact text from the copy"", ""explanation"": ""clear reason this deviates from the brand voice""}]}";

            // ── Rewrite stage ─────────────────────────────────────────────────

            public const string RewriteSystemPrompt =
                "You are a brand voice copywriter. Generate concise, drop-in rewrite suggestions " +
                "for flagged brand voice violations. Always respond with valid JSON only.";

            public const string RewritePrompt = @"You are fixing brand voice violations in marketing copy. Below are the identified violations and the original copy.

Brand: {{brand_name}}
Content type: {{copy_type}}

Original copy:
{{copy_text}}

Violations to fix:
{{violations}}

TASK:
For each violation provide a specific, drop-in replacement excerpt that corrects the deviation while preserving the original intent and meaning. Keep rewrites concise and ready to paste directly into the document.

After reviewing all violations assign:
- overallRating: ""compliant"" (no significant issues), ""minor"" (low-severity issues only), ""moderate"" (some medium or high-severity issues), or ""major"" (multiple high-severity violations)
- summary: 1–2 sentence plain-language description of the main issues found
- approvedForPublishing: true only if overallRating is ""compliant"" or ""minor"" and there are no high-severity violations

Respond ONLY with valid JSON (no markdown, no code fences):
{""overallRating"": ""moderate"", ""summary"": ""..."", ""approvedForPublishing"": false, ""violations"": [{""category"": ""tone"", ""severity"": ""high"", ""excerpt"": ""..."", ""explanation"": ""..."", ""suggestedRewrite"": ""...""}]}";
        }

        public static class Messages
        {
            public const string EmptyCopyText          = "Marketing copy cannot be empty.";
            public const string EmptyBrandGuidelines   = "Brand guidelines cannot be empty.";
            public const string CopyTooLong            = "Marketing copy must not exceed 8,000 characters.";
            public const string GuidelinesTooLong      = "Brand guidelines must not exceed 5,000 characters.";
            public const string OpenAiKeyNotConfigured = "OpenAI API key is not configured.";
            public const string WorkflowFailed         = "Brand check failed. Please try again.";
            public const string UnexpectedError        = "An unexpected error occurred.";
        }
    }
}
