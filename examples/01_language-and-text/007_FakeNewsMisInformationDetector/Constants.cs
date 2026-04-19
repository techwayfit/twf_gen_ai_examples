namespace _007_FakeNewsMisInformationDetector
{
    public static class Constants
    {
        public static class Prompts
        {
            // ── Claim extraction stage ────────────────────────────────────────────

            public const string ClaimExtractionSystemPrompt =
                "You are a professional fact-checker. Your job is to identify specific, " +
                "verifiable factual claims in articles. Always respond with valid JSON only.";

            public const string ClaimExtractionPrompt = @"Read the article below and identify the key factual claims that can be verified against external sources.

Article:
{{article_text}}

Rules:
- Extract only specific, factual assertions (not opinions, predictions, or editorial judgements)
- Each claim must be self-contained and usable as a standalone search query
- Prefer the most significant and checkable claims in the article
- Return at most {{max_claims}} claims

Respond ONLY with valid JSON (no markdown, no code fences):
{""claims"": [""First factual claim here"", ""Second factual claim here"", ""Third factual claim here""]}";

            // ── Claim evaluation stage (chain-of-thought) ─────────────────────────

            public const string EvaluationSystemPrompt =
                "You are an expert fact-checker. Reason step-by-step through the evidence and " +
                "return a structured credibility assessment. Always respond with valid JSON only.";

            public const string EvaluationPrompt = @"You are fact-checking an article. Below are the key claims extracted from it, each paired with search evidence retrieved from the web.

ARTICLE:
{{article_text}}

CLAIMS AND EVIDENCE:
{{claims_with_evidence}}

TASK:
For each claim, reason through the evidence and assign a verdict:
- ""supported"": credible sources clearly confirm the claim
- ""disputed"": credible sources contradict or cast significant doubt on the claim
- ""unverifiable"": insufficient or irrelevant evidence was found

Confidence: a number from 0.0 to 1.0 indicating how certain you are in the verdict.

For each source you cite, use only sources that appear in the evidence above.

After evaluating all claims individually, assign an OVERALL credibility score (0–100) and overall verdict:
- 80-100: mostly supported
- 50-79: mixed — some claims supported, others disputed or unverifiable
- 20-49: likely misleading — majority of key claims disputed or unverifiable
- 0-19: likely false — most key claims directly contradicted by credible evidence

Respond ONLY with valid JSON (no markdown, no code fences):
{""overall_credibility_score"": 25, ""overall_verdict"": ""likely false"", ""summary"": ""2-3 sentence plain-language verdict explaining the key issues found."", ""claim_verdicts"": [{""id"": ""1"", ""text"": ""the claim text"", ""verdict"": ""disputed"", ""confidence"": 0.9, ""reasoning"": ""2-3 sentence step-by-step reasoning based on the evidence."", ""sources"": [{""title"": ""source title"", ""url"": ""source url"", ""snippet"": ""relevant excerpt""}]}]}";
        }

        public static class Messages
        {
            public const string EmptyArticle    = "Article text cannot be empty.";
            public const string ArticleTooLong  = "Article text must not exceed 10 000 characters.";
            public const string OpenAiKeyNotConfigured  = "OpenAI API key is not configured.";
            public const string SearchApiKeyNotConfigured = "Search API key is not configured.";
            public const string WorkflowFailed  = "Fact-check failed. Please try again.";
            public const string UnexpectedError = "An unexpected error occurred.";
        }
    }
}
