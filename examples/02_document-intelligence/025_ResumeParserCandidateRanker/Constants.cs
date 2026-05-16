namespace _025_ResumeParserCandidateRanker;

public static class Constants
{
    public static class Prompts
    {
        // ── Profile extraction ────────────────────────────────────────────────

        public const string ProfileExtractionSystemPrompt =
            "You are a precise resume parser. Extract structured information from the provided resume text. " +
            "Return only valid JSON with no markdown code fences or extra commentary.";

        public const string ProfileExtractionPrompt =
            @"Resume text:
{{resume_text}}

Extract the following fields from this resume and return ONLY valid JSON (no markdown, no code fences):
{
  ""name"": ""Full Name"",
  ""email"": ""email@example.com or empty string"",
  ""phone"": ""phone number or empty string"",
  ""location"": ""city/country or empty string"",
  ""summary"": ""brief professional summary in 1–3 sentences"",
  ""skills"": [""skill1"", ""skill2""],
  ""experience"": [
    {
      ""title"": ""Job Title"",
      ""company"": ""Company Name"",
      ""duration"": ""e.g. 2020–2023 or 3 years"",
      ""summary"": ""brief description of responsibilities and achievements""
    }
  ],
  ""education"": [
    {
      ""degree"": ""Degree Name"",
      ""institution"": ""University Name"",
      ""year"": ""graduation year or date range""
    }
  ]
}";

        // ── Interview question generation ─────────────────────────────────────

        public const string InterviewQuestionsSystemPrompt =
            "You are an experienced technical interviewer. Generate targeted interview questions for a candidate " +
            "based on their specific background and the job requirements. " +
            "Questions must be concrete, behavioral or technical, and grounded in the candidate's actual experience. " +
            "Return only valid JSON with no markdown code fences or extra commentary.";

        public const string InterviewQuestionsPrompt =
            @"Job requirements:
{{job_description}}

Candidate profile:
Name: {{candidate_name}}
Skills: {{candidate_skills}}
Experience:
{{candidate_experience}}

Generate exactly 5 targeted interview questions for this candidate that:
1. Are specific to their experience and the job requirements
2. Include a mix of technical and behavioural questions
3. Probe areas where their background directly overlaps with the role

Return ONLY valid JSON (no markdown, no code fences):
{""questions"": [""question 1"", ""question 2"", ""question 3"", ""question 4"", ""question 5""]}";
    }

    public static class Messages
    {
        public const string EmptyJobDescription    = "Job description cannot be empty.";
        public const string NoResumesProvided      = "At least one resume file must be provided.";
        public const string OpenAiKeyNotConfigured = "OpenAI API key is not configured. Add it to appsettings.local.json.";
        public const string WorkflowFailed         = "Ranking workflow failed. Please try again.";
        public const string UnexpectedError        = "An unexpected error occurred.";
    }
}
