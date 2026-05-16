namespace _032_TextbookChapterQuestionGenerator;

public static class Constants
{
    public static class Prompts
    {
        // ── Content analysis ──────────────────────────────────────────────────

        public const string ContentAnalysisSystemPrompt =
            "You are an expert {{subject}} educator and curriculum designer. Analyse the provided textbook chapter and " +
            "identify the key concepts, topics, and vocabulary. Map each topic to the most appropriate " +
            "Bloom's Taxonomy cognitive level: Remember, Understand, Apply, Analyse, Evaluate, or Create. " +
            "Return only valid JSON with no markdown code fences or extra commentary.";

        public const string ContentAnalysisPrompt =
            @"Textbook chapter text:
{{chapter_text}}

Analyse this chapter and return ONLY valid JSON (no markdown, no code fences):
{
  ""topics"": [""topic1"", ""topic2""],
  ""key_terms"": [""term1"", ""term2""],
  ""bloom_levels"": [""Remember"", ""Understand"", ""Apply""],
  ""summary"": ""brief 2–3 sentence summary of the chapter content""
}";

        // ── MCQ generation ────────────────────────────────────────────────────

        public const string McqSystemPrompt =
            "You are an expert {{subject}} assessment writer. Generate multiple-choice questions (MCQs) from the provided " +
            "textbook chapter content, calibrated to the specified Bloom's Taxonomy levels. Each question must " +
            "have exactly one unambiguously correct answer and three plausible but clearly incorrect distractors. " +
            "Return only valid JSON with no markdown code fences or extra commentary.";

        public const string McqPrompt =
            @"Chapter content:
{{chapter_text}}

Content analysis:
{{content_analysis}}

Target Bloom's Taxonomy levels: {{bloom_levels}}
Target difficulty: {{difficulty}}
Number of questions to generate: {{count}}

Generate exactly {{count}} MCQs. Return ONLY valid JSON (no markdown, no code fences):
{
  ""questions"": [
    {
      ""question"": ""Question text here?"",
      ""correct_answer"": ""The correct answer"",
      ""distractors"": [""Wrong answer 1"", ""Wrong answer 2"", ""Wrong answer 3""],
      ""bloom_level"": ""Remember"",
      ""difficulty"": ""easy""
    }
  ]
}";

        // ── Short-answer generation ───────────────────────────────────────────

        public const string ShortAnswerSystemPrompt =
            "You are an expert {{subject}} assessment writer. Generate short-answer questions from the provided textbook " +
            "chapter content, calibrated to the specified Bloom's Taxonomy levels. Each question should require " +
            "a focused response of 2–5 sentences. Include a model answer and marking criteria. " +
            "Return only valid JSON with no markdown code fences or extra commentary.";

        public const string ShortAnswerPrompt =
            @"Chapter content:
{{chapter_text}}

Content analysis:
{{content_analysis}}

Target Bloom's Taxonomy levels: {{bloom_levels}}
Target difficulty: {{difficulty}}
Number of questions to generate: {{count}}

Generate exactly {{count}} short-answer questions. Return ONLY valid JSON (no markdown, no code fences):
{
  ""questions"": [
    {
      ""question"": ""Question text here?"",
      ""model_answer"": ""A complete model answer of 2–5 sentences."",
      ""marking_criteria"": ""Award 1 mark for X, 1 mark for Y, 1 mark for Z."",
      ""bloom_level"": ""Understand"",
      ""difficulty"": ""medium""
    }
  ]
}";

        // ── Essay generation ──────────────────────────────────────────────────

        public const string EssaySystemPrompt =
            "You are an expert {{subject}} assessment writer. Generate essay prompts from the provided textbook chapter " +
            "content, calibrated to higher-order Bloom's Taxonomy levels (Analyse, Evaluate, Create). " +
            "Each prompt should require extended analysis or argument. Include a scoring rubric and a suggested " +
            "word count. Return only valid JSON with no markdown code fences or extra commentary.";

        public const string EssayPrompt =
            @"Chapter content:
{{chapter_text}}

Content analysis:
{{content_analysis}}

Target Bloom's Taxonomy levels: {{bloom_levels}}
Target difficulty: {{difficulty}}
Number of prompts to generate: {{count}}

Generate exactly {{count}} essay prompts. Return ONLY valid JSON (no markdown, no code fences):
{
  ""questions"": [
    {
      ""prompt"": ""Essay prompt here."",
      ""rubric"": ""Excellent (18–20): ... Good (14–17): ... Satisfactory (10–13): ..."",
      ""suggested_word_count"": 500,
      ""bloom_level"": ""Evaluate"",
      ""difficulty"": ""hard""
    }
  ]
}";

        // ── True/false generation ─────────────────────────────────────────────

        public const string TrueFalseSystemPrompt =
            "You are an expert {{subject}} assessment writer. Generate true/false statements from the provided textbook " +
            "chapter content, calibrated to the specified Bloom's Taxonomy levels. Produce a balanced mix of " +
            "true and false statements. Include a justification explaining why the statement is true or false. " +
            "Return only valid JSON with no markdown code fences or extra commentary.";

        public const string TrueFalsePrompt =
            @"Chapter content:
{{chapter_text}}

Content analysis:
{{content_analysis}}

Target Bloom's Taxonomy levels: {{bloom_levels}}
Target difficulty: {{difficulty}}
Number of statements to generate: {{count}}

Generate exactly {{count}} true/false statements. Return ONLY valid JSON (no markdown, no code fences):
{
  ""questions"": [
    {
      ""statement"": ""Statement text here."",
      ""is_true"": true,
      ""justification"": ""Explanation of why this statement is true or false."",
      ""bloom_level"": ""Remember"",
      ""difficulty"": ""easy""
    }
  ]
}";
        // ── Word problem generation ───────────────────────────────────────────

        public const string WordProblemSystemPrompt =
            "You are an expert {{subject}} educator specialising in problem-based assessment. Generate word problems from the provided textbook chapter. " +
            "Use LaTeX delimiters for ALL mathematical expressions: $...$ for inline math, $$...$$ for display equations. " +
            "Include an optional inline SVG diagram (self-contained, viewBox-based, no external assets) only when it " +
            "genuinely aids understanding (e.g. geometry shapes, number lines, coordinate grids). " +
            "Set svg_diagram to an empty string when no diagram is needed. " +
            "Return only valid JSON with no markdown code fences or extra commentary.";

        public const string WordProblemPrompt =
            @"Chapter content:
{{chapter_text}}

Content analysis:
{{content_analysis}}

Target Bloom's Taxonomy levels: {{bloom_levels}}
Target difficulty: {{difficulty}}
Number of problems to generate: {{count}}

Generate exactly {{count}} math word problems. Return ONLY valid JSON (no markdown, no code fences):
{
  ""questions"": [
    {
      ""problem"": ""A train travels at $60$ km/h for $1.5$ hours. How far does it travel?"",
      ""hint"": ""Use the formula: $d = v \times t$"",
      ""solution_steps"": [
        ""Identify the values: $v = 60$ km/h, $t = 1.5$ h"",
        ""Apply the formula: $d = 60 \times 1.5$"",
        ""Calculate: $d = 90$ km""
      ],
      ""final_answer"": ""$d = 90$ km"",
      ""svg_diagram"": """",
      ""bloom_level"": ""Apply"",
      ""difficulty"": ""easy""
    }
  ]
}";

    }

    public static class Messages
    {
        public const string EmptyChapterText        = "Chapter text cannot be empty.";
        public const string NoQuestionsRequested    = "At least one question count must be greater than zero.";
        public const string OpenAiKeyNotConfigured  = "OpenAI API key is not configured. Add it to appsettings.local.json.";
        public const string WorkflowFailed          = "Question generation workflow failed. Please try again.";
        public const string UnexpectedError         = "An unexpected error occurred.";
    }
}
