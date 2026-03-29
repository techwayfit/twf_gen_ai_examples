# Language & Text Applications — Examples #1–20

Single-turn and multi-turn LLM applications focused on text generation, transformation, and analysis. Start here to master the core TwfAiFramework pipeline patterns.

---

## Examples

### #1 — Contextual Customer Support Chatbot
Multi-turn support bot that maintains conversation history, classifies intent, and escalates to human agents when confidence is low.

**Key patterns:** `MaintainHistory = true` on `LlmNode`, `ConditionNode` for escalation routing, `Workflow.Branch()` for handoff logic.

---

### #2 — Intelligent Email Drafting Assistant
Reads incoming emails, detects sentiment and urgency, and generates tone-appropriate replies.

**Key patterns:** `PromptBuilderNode` with tone templates, `OutputParserNode` to extract sentiment and urgency flags, `ConditionNode` for tone selection.

---

### #3 — Automated Newsletter Generator
Ingests RSS feeds, clusters articles by topic, summarises insights, and generates a personalised newsletter.

**Key patterns:** `HttpRequestNode` for feed fetching, `Workflow.ForEach()` over articles, `LlmNode` for per-article summarisation, `TransformNode` for final assembly.

---

### #4 — Real-Time Multilingual Translation Hub
Translates text preserving cultural nuance, idiomatic expressions, and domain terminology, with back-translation quality validation.

**Key patterns:** Two `LlmNode` steps (translate → back-translate), `OutputParserNode` for quality score extraction, `NodeOptions.WithRetry()` on API calls.

---

### #5 — Long-Form Content Writer with SEO Optimization
Researches competing content via web search, then generates SEO-optimised articles with structured headings and meta descriptions.

**Key patterns:** `HttpRequestNode` for search API, `PromptBuilderNode` with multi-step templates (outline → draft → meta), chained `LlmNode` calls.

---

### #6 — Brand Voice Consistency Checker
Reviews marketing copy against brand guidelines and flags tone, vocabulary, and style deviations.

**Key patterns:** `EmbeddingNode` for style comparison, `OutputParserNode` to extract violation categories, `TransformNode` to format feedback.

---

### #7 — Fake News & Misinformation Detector
Cross-references article claims against verified sources, assigns a credibility score, and provides citation evidence.

**Key patterns:** `HttpRequestNode` for real-time search grounding, `LlmNode` with chain-of-thought reasoning, `OutputParserNode` for structured credibility report.

---

### #8 — Personalized Children's Story Generator
Creates age-appropriate, personalised stories based on a child's name, interests, and moral lessons, plus image-prompt suggestions.

**Key patterns:** `PromptBuilderNode` with dynamic variables (`{{child_name}}`, `{{interest}}`), `LlmNode`, `OutputParserNode` for story + image-prompt fields.

---

### #9 — Semantic Job Description Analyzer
Parses job descriptions, extracts required vs. preferred skills, detects inclusive/exclusive language, and matches against candidate profiles.

**Key patterns:** `OutputParserNode` with `fieldMapping` for skill extraction, `EmbeddingNode` for candidate matching, `ConditionNode` for bias flagging.

---

### #10 — Academic Paper Abstract Generator
Generates publication-quality abstracts with IMRAD structure and keyword extraction from full-paper input.

**Key patterns:** `TransformNode` for chunking long papers, `PromptBuilderNode` with IMRAD template, `OutputParserNode` to extract abstract + keywords.

---

### #11 — Debate Coach & Argument Strengthener
Identifies logical fallacies, strengthens arguments, generates counterarguments with evidence, and provides rebuttal strategy.

**Key patterns:** `Workflow.Parallel()` to generate both strengthened argument and counterarguments simultaneously, `LlmNode` with chain-of-thought, `HttpRequestNode` for evidence retrieval.

---

### #12 — Meeting Notes Summarizer & Action Item Extractor
Processes meeting transcripts, identifies speakers, summarises by topic, and extracts action items with owners and deadlines.

**Key patterns:** `TransformNode` for transcript segmentation, `OutputParserNode` with `fieldMapping` for structured action items, `HttpRequestNode` to sync to project management APIs.

---

### #13 — Legal Contract Plain-Language Explainer
Explains contract clauses in plain English, flags unfavourable terms, and produces a risk summary.

**Key patterns:** `Workflow.ForEach()` over contract clauses, `LlmNode` per clause, `MergeNode` to combine results, `ConditionNode` for risk-level flagging.

---

### #14 — Personalized Learning Path Generator
Assesses learner knowledge via conversation, identifies gaps, and generates a personalised curriculum.

**Key patterns:** Multi-turn `LlmNode` with `MaintainHistory`, `OutputParserNode` for knowledge-gap extraction, `TransformNode` for curriculum assembly.

---

### #15 — Product Description Generator for E-Commerce
Bulk-generates SEO-optimised product descriptions in multiple tones from SKU data.

**Key patterns:** `Workflow.ForEach()` over SKU list, `PromptBuilderNode` with tone-selection variable, `DelayNode.RateLimitDelay()` for API rate limits.

---

### #16 — Mental Health Journaling Companion
Prompts reflective questions, identifies emotional patterns, provides CBT-based reframing, and flags when professional help may be needed.

**Key patterns:** Multi-turn `LlmNode`, `OutputParserNode` for mood/emotion extraction, `ConditionNode` + `Workflow.Branch()` for safety escalation.

---

### #17 — Historical Document Interpreter
Transcribes and modernises archaic text, provides historical context, and answers questions about the content.

**Key patterns:** `HttpRequestNode` for OCR API, `PromptBuilderNode` with context-injection template, `LlmNode` for modernisation + Q&A.

---

### #18 — LinkedIn Profile Optimizer
Scores a profile against a target role, rewrites sections for ATS compatibility, and suggests skills to add.

**Key patterns:** `OutputParserNode` for structured scoring, `Workflow.Parallel()` to rewrite multiple sections simultaneously, `TransformNode` to merge rewrites.

---

### #19 — Socratic Tutoring System
Teaches through Socratic questions, adapts difficulty based on student responses, never gives direct answers.

**Key patterns:** Multi-turn `LlmNode`, `WorkflowContext` global state for difficulty tracking, `ConditionNode` for hint-level routing.

---

### #20 — Sales Call Script Generator & Objection Handler
Generates personalised cold call scripts and anticipates objections with evidence-based rebuttals.

**Key patterns:** `HttpRequestNode` for CRM data, `PromptBuilderNode` with prospect-persona template, `Workflow.Parallel()` for script + objection-handler generation.

---

## TwfAiFramework Quickstart for This Category

```csharp
// Typical pattern: PromptBuilder → LLM → OutputParser
var result = await Workflow.Create("TextApp")
    .UseLogger(logger)
    .AddNode(new PromptBuilderNode(
        promptTemplate: "{{system_instruction}}\n\nUser: {{user_input}}",
        systemTemplate: "You are a helpful assistant."))
    .AddNode(new LlmNode(new LlmConfig
    {
        Provider = "openai",
        Model    = "gpt-4o",
        ApiKey   = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!
    }))
    .AddNode(new OutputParserNode())   // optional — for structured JSON output
    .RunAsync(new WorkflowData()
        .Set("user_input", "Hello, I need help with my order.")
        .Set("system_instruction", "You are a friendly e-commerce support agent."));
```
