namespace _032_TextbookChapterQuestionGenerator;

public static class Constants
{
    public static class Prompts
    {
        // ── Content analysis ──────────────────────────────────────────────────

        public const string ContentAnalysisSystemPrompt =
          "You are an expert {{subject}} educator and curriculum designer. Analyse the provided textbook chapter and " +
          "identify its most assessable concepts, subtopics, and technical vocabulary. Prioritise concepts that are " +
          "central, frequently referenced, or prerequisite for understanding later ideas. Map each identified topic to " +
          "the most appropriate Bloom's Taxonomy cognitive level: Remember, Understand, Apply, Analyse, Evaluate, or " +
          "Create. Use only information present in the chapter; do not introduce external facts. Return only valid JSON " +
          "with no markdown code fences or extra commentary.";

        public const string ContentAnalysisPrompt =
            @"Textbook chapter text:
{{chapter_text}}

Analyse this chapter and return ONLY valid JSON (no markdown, no code fences):
{
  ""topics"": [""topic1"", ""topic2""],
  ""key_terms"": [""term1"", ""term2""],
  ""bloom_levels"": [""Remember"", ""Understand"", ""Apply""],
  ""summary"": ""brief 2-3 sentence summary of the chapter content""
}

Quality requirements:
- Include 6-12 concrete topics, not broad labels like ""introduction"" or ""overview"".
- Include key terms that are specific and reusable in assessment questions.
- Ensure topics and key terms are chapter-grounded and non-duplicative.
- Keep summary concise and faithful to the chapter scope only.";

        // ── MCQ generation ────────────────────────────────────────────────────

        public const string McqSystemPrompt =
            "You are an expert {{subject}} assessment writer. Generate multiple-choice questions (MCQs) from the provided " +
            "textbook chapter content, calibrated to the specified Bloom's Taxonomy levels. Each question must " +
            "have exactly one unambiguously correct answer and three plausible but clearly incorrect distractors. " +
          "Questions must be specific, non-trivial, and answerable from the chapter alone. Avoid vague wording, " +
          "testwise clues, and repeated stems. Return only valid JSON with no markdown code fences or extra commentary.";

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
}

Quality requirements:
- Generate exactly {{count}} items, no more and no fewer.
- Cover different topics from the content analysis where possible; avoid near-duplicate questions.
- Prefer conceptually diagnostic questions over pure memorisation unless Bloom level is Remember.
- Keep each stem clear, specific, and typically under 30 words.
- Distractors must be plausible within the same topic/domain, similar in length/style, and mutually distinct.
- Do not use ""all of the above"", ""none of the above"", trick phrasing, or negatives like ""EXCEPT"" unless unavoidable.
- Ensure the correct answer is not copied verbatim from a distractor and is not ambiguous.
- Use only the provided Bloom levels and difficulty setting.";

        // ── Short-answer generation ───────────────────────────────────────────

        public const string ShortAnswerSystemPrompt =
            "You are an expert {{subject}} assessment writer. Generate short-answer questions from the provided textbook " +
            "chapter content, calibrated to the specified Bloom's Taxonomy levels. Each question should require " +
            "a focused response of 2–5 sentences. Include a model answer and marking criteria. " +
          "Questions must require reasoning, not just definition recall, unless Bloom level is Remember. Return only " +
          "valid JSON with no markdown code fences or extra commentary.";

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
}

Quality requirements:
- Generate exactly {{count}} items, no more and no fewer.
- Keep questions specific, chapter-grounded, and non-overlapping.
- Write model answers as concise, accurate, complete responses (typically 2-5 sentences).
- Marking criteria should be point-based and observable (what earns marks), not generic advice.
- Avoid yes/no prompts and avoid prompts that only ask to list facts unless Bloom level is Remember.
- Use only the provided Bloom levels and difficulty setting.";

        // ── Essay generation ──────────────────────────────────────────────────

        public const string EssaySystemPrompt =
            "You are an expert {{subject}} assessment writer. Generate essay prompts from the provided textbook chapter " +
            "content, calibrated to higher-order Bloom's Taxonomy levels (Analyse, Evaluate, Create). " +
            "Each prompt should require extended analysis or argument. Include a scoring rubric and a suggested " +
          "word count. Prompts must be debatable or synthesising, not simple summaries. Return only valid JSON with " +
          "no markdown code fences or extra commentary.";

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
}

Quality requirements:
- Generate exactly {{count}} items, no more and no fewer.
- Each prompt should demand analysis, evaluation, or creation, and cite chapter concepts explicitly.
- Avoid prompts that can be answered by paraphrasing a single paragraph.
- Rubric should be criterion-based with clear performance bands and specific expectations.
- suggested_word_count should be realistic for the cognitive demand and difficulty.
- Use only the provided Bloom levels and difficulty setting.";

        // ── True/false generation ─────────────────────────────────────────────

        public const string TrueFalseSystemPrompt =
            "You are an expert {{subject}} assessment writer. Generate true/false statements from the provided textbook " +
            "chapter content, calibrated to the specified Bloom's Taxonomy levels. Produce a balanced mix of " +
            "true and false statements. Include a justification explaining why the statement is true or false. " +
          "Statements must test conceptual precision, not trivial wording quirks. Return only valid JSON with no " +
          "markdown code fences or extra commentary.";

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
}

Quality requirements:
- Generate exactly {{count}} items, no more and no fewer.
- Maintain an approximately balanced mix of true and false statements.
- Avoid absolute cues (always/never) unless they are genuinely correct in context.
- Make false statements subtly incorrect, not absurdly wrong.
- Justifications should cite the precise concept that determines truth value.
- Use only the provided Bloom levels and difficulty setting.";
        // ── Word problem generation ───────────────────────────────────────────

        public const string WordProblemSystemPrompt =
            "You are an expert {{subject}} educator specialising in problem-based assessment. Generate word problems from the provided textbook chapter. " +
            "Use LaTeX delimiters for ALL mathematical expressions: $...$ for inline math, $$...$$ for display equations. " +
            "Include an optional inline SVG diagram (self-contained, viewBox-based, no external assets) only when it " +
            "genuinely aids understanding (e.g. geometry shapes, number lines, coordinate grids). " +
            "Set svg_diagram to an empty string when no diagram is needed. " +
          "Problems must be realistic, unambiguous, and solvable using chapter concepts. Return only valid JSON with no " +
          "markdown code fences or extra commentary.";

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
}

Quality requirements:
- Generate exactly {{count}} items, no more and no fewer.
- Keep problem statements clear, with all required values and units provided.
- Ensure each problem is solvable from given information without hidden assumptions.
- solution_steps should be logically ordered and complete enough for partial-credit marking.
- final_answer must include units when applicable and match the worked steps.
- Use only the provided Bloom levels and difficulty setting.";

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
