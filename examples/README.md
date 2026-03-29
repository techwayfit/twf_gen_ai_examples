# TwfAiFramework — Examples

This folder contains 100 real-world GenAI application examples organised by domain. Each category folder has its own README with per-example descriptions, key learning objectives, and TwfAiFramework implementation patterns.

---

## Categories

| # | Folder | Examples | Difficulty |
|---|--------|----------|------------|
| 1 | [Language & Text Applications](01_language-and-text/README.md) | #1–20 | Beginner → Intermediate |
| 2 | [Document Intelligence](02_document-intelligence/README.md) | #21–35 | Intermediate |
| 3 | [Code & Developer Tools](03_code-and-developer-tools/README.md) | #36–50 | Intermediate |
| 4 | [Multimodal & Vision Applications](04_multimodal-and-vision/README.md) | #51–60 | Intermediate |
| 5 | [Audio, Voice & Speech](05_audio-voice-and-speech/README.md) | #61–68 | Intermediate |
| 6 | [Agents & Autonomous Systems](06_agents-and-autonomous-systems/README.md) | #69–80 | Advanced |
| 7 | [Data, Analytics & Search](07_data-analytics-and-search/README.md) | #81–90 | Advanced |
| 8 | [Domain-Specific Applications](08_domain-specific-applications/README.md) | #91–100 | Advanced |

---

## Learning Roadmap

```
Level 1 — Foundations            Examples #1–20, #36–38
  Single-turn LLM apps, prompt engineering, basic API integrations

Level 2 — RAG & Documents        Examples #21–35
  Vector databases, chunking, retrieval-augmented generation

Level 3 — Multimodal & Voice     Examples #51–68
  Vision-language models, ASR, TTS pipelines

Level 4 — Agentic Systems        Examples #69–80
  Autonomous agents, tool use, multi-step planning

Level 5 — Production & Domain    Examples #81–100
  Domain-specific systems, evaluation, monitoring, responsible AI
```

---

## Core TwfAiFramework Patterns Used Across Examples

| Pattern | Nodes / Features |
|---|---|
| Single LLM call | `PromptBuilderNode` → `LlmNode` |
| Structured output | `LlmNode` → `OutputParserNode` |
| RAG pipeline | `EmbeddingNode` + `HttpRequestNode` → `PromptBuilderNode` → `LlmNode` |
| Conditional routing | `ConditionNode` + `Workflow.Branch()` |
| Parallel processing | `Workflow.Parallel()` |
| Batch iteration | `Workflow.ForEach()` |
| External API calls | `HttpRequestNode` |
| Custom transforms | `TransformNode` / `BaseNode` subclass |
| Rate limiting | `DelayNode.RateLimitDelay()` |
| Retry on failure | `NodeOptions.WithRetry(n)` |

---

## Full Example List

See [100_GenAI_Examples.md](100_GenAI_Examples.md) for the complete catalogue with detailed descriptions and learning objectives for all 100 examples.
