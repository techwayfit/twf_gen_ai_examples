namespace _008_PersonalizedChildrenStoryGenerator
{
    public static class Constants
    {
        public static class Prompts
        {
            // ── Story generation stage ────────────────────────────────────────

            public const string StorySystemPrompt =
                "You are a talented children's book author who writes warm, imaginative, age-appropriate stories. " +
                "You always adapt vocabulary and sentence complexity to the specified reading level. " +
                "Always respond with valid JSON only.";

            public const string StoryPromptTemplate = @"Write a {{story_length}} children's story for a {{reading_level}} reader.

Story details:
- The main character is a child named {{child_name}} who loves {{interest}}
- The story must clearly convey the moral lesson: ""{{moral_lesson}}""
- Write the story in {{language}}
- Keep the vocabulary and sentence structure appropriate for a {{reading_level}} reader:
  * toddler: very short sentences, simple words, repetitive patterns, 150–250 words
  * early-reader: short sentences, familiar vocabulary, clear narrative arc, 300–450 words
  * middle-grade: richer vocabulary, descriptive language, subplot allowed, 500–700 words

Guidelines:
- Weave {{child_name}}'s love of {{interest}} naturally into the setting or plot — do not just mention it once
- The moral lesson should emerge organically from the story events, not be stated as a lesson at the end
- End the story on a warm, positive note
- Include 2–4 vivid scenes suitable for illustration

Return ONLY valid JSON (no markdown, no code fences) with this exact structure:
{""title"": ""A creative story title"", ""storyText"": ""The full narrative text"", ""imagePrompts"": [""Scene 1 description for an illustrator..."", ""Scene 2 description...""], ""moralHighlight"": ""One child-friendly sentence restating the moral"", ""wordCount"": 312}";
        }

        public static class Messages
        {
            public const string EmptyChildName        = "Child's name cannot be empty.";
            public const string EmptyInterest         = "Interest cannot be empty.";
            public const string EmptyMoralLesson      = "Moral lesson cannot be empty.";
            public const string ChildNameTooLong      = "Child's name must not exceed 50 characters.";
            public const string InterestTooLong       = "Interest must not exceed 100 characters.";
            public const string MoralLessonTooLong    = "Moral lesson must not exceed 200 characters.";
            public const string OpenAiKeyNotConfigured = "OpenAI API key is not configured.";
            public const string WorkflowFailed        = "Story generation failed. Please try again.";
            public const string UnexpectedError       = "An unexpected error occurred.";
        }
    }
}
