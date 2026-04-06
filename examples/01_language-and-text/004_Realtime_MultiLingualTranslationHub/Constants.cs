namespace _004_Realtime_MultiLingualTranslationHub
{
    public static class Constants
    {
        public static class Prompts
        {
            public const string TranslationSystemPrompt =
                "You are an expert translator with deep cultural knowledge across many languages and domains. Always respond with valid JSON only.";

            public const string TranslationPrompt = @"Translate the following text into {{target_language_name}}.

Domain: {{domain}}
{{glossary_hints}}
Source text:
{{source_text}}

Perform a careful chain-of-thought translation:
1. Detect the source language
2. Identify idioms, cultural references, or domain-specific terms
3. Consider the appropriate register and cultural context for {{target_language_name}}
4. Apply domain terminology consistently

Respond ONLY with valid JSON (no markdown, no code block):
{""source_language"": ""detected source language name"", ""translation"": ""your translation here"", ""cultural_notes"": ""brief note on cultural adaptations made, or empty string"", ""flagged_terms"": ""comma-separated terms with low confidence, or empty string""}";

            public const string BackTranslationSystemPrompt =
                "You are a professional translator. Always respond with valid JSON only.";

            public const string BackTranslationPrompt = @"Translate the following {{target_language_name}} text back into {{source_language}}.

Text to translate back:
{{translation}}

After translating, score the semantic quality compared to typical source text for this type of content.
A score of 90-100 means the back-translation perfectly preserves the original meaning.
A score of 75-89 means minor differences but meaning is intact.
A score of 50-74 means noticeable meaning loss.
A score below 50 means significant meaning distortion.

Respond ONLY with valid JSON (no markdown, no code block):
{""back_translation"": ""back-translated text here"", ""quality_score"": 85}";

            public const string RefinementSystemPrompt =
                "You are an expert translator specialising in quality improvement. Always respond with valid JSON only.";

            public const string RefinementPrompt = @"The following translation has quality issues. Improve it.

Original text ({{source_language}}):
{{source_text}}

Target language: {{target_language_name}}
Current translation: {{translation}}
Back-translation revealed: {{back_translation}}
Terms to review: {{flagged_terms}}

Provide an improved translation that:
1. Better preserves the original meaning
2. Correctly handles the flagged terms
3. Maintains appropriate cultural context and register

Respond ONLY with valid JSON (no markdown, no code block):
{""translation"": ""improved translation here""}";
        }

        public static class GlossaryHints
        {
            public const string Legal =
                "Glossary guidance — preserve these legal terms accurately: force majeure, indemnify/indemnification, jurisdiction, liability, covenant, plaintiff, defendant, arbitration, breach of contract, due diligence.";

            public const string Medical =
                "Glossary guidance — use standard medical terminology: diagnosis, prognosis, aetiology, contraindication, pharmacokinetics, pathology, haemoglobin, myocardial infarction, analgesia, prophylaxis.";

            public const string Technical =
                "Glossary guidance — use industry-standard technical terms where possible; borrow English terms (e.g. API, pipeline, framework, cloud) when no established equivalent exists in the target language.";

            public const string Finance =
                "Glossary guidance — use precise financial terminology: revenue, gross margin, net profit, cash flow, balance sheet, accounts payable, accounts receivable, liquidity, amortization, equity.";

            public const string Marketing =
                "Glossary guidance — preserve marketing intent and standard terms: brand awareness, value proposition, conversion rate, call to action, lead generation, customer journey, positioning, segmentation, campaign, retention.";

            public const string Education =
                "Glossary guidance — use clear academic terminology: curriculum, learning outcomes, assessment, formative feedback, summative evaluation, pedagogy, syllabus, credit hours, prerequisite, competency.";

            public const string Ecommerce =
                "Glossary guidance — use common e-commerce terms consistently: checkout, shopping cart, SKU, fulfillment, return policy, discount code, conversion funnel, average order value, shipping method, inventory.";

            public const string General = "";
        }

        public static class Messages
        {
            public const string EmptySourceText = "Source text cannot be empty";
            public const string TextTooLong = "Source text must not exceed 5000 characters";
            public const string UnsupportedLanguage = "Unsupported target language";
            public const string OpenApiKeyNotConfigured = "OpenAI API key not configured";
            public const string TranslationFailed = "Translation could not be completed. Please try again.";
            public const string UnexpectedError = "An unexpected error occurred";
        }
    }
}
