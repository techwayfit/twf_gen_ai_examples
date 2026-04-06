# 004 - Real-Time Multilingual Translation Hub

## Project Overview

This example builds a real-time multilingual translation service for **TechWayFit**, using ASP.NET Core Blazor Server and the **TwfAiFramework**. The system accepts source text and a target language, produces a culturally nuanced translation that preserves idiomatic expressions and domain-specific terminology (general, legal, medical, technical, finance, marketing, education, ecommerce), and automatically validates quality through a back-translation round-trip.

A translator submits source text once. The pipeline detects the source language, translates with chain-of-thought reasoning for nuance, scores the result against a back-translation, and returns a rich response with the final translation, quality score, and any flagged terminology issues.

## Objective

Demonstrate a production-grade **multi-stage LLM translation pipeline** that goes beyond word-for-word substitution:

- **Language detection** — the translation stage identifies the source language and preserves it in structured output
- **Chain-of-thought nuanced translation** — a `PromptBuilderNode` + `LlmNode` pair instructs the LLM to reason through idioms, cultural references, and domain terminology before producing the final translation
- **Back-translation validation** — a second `LlmNode` translates the result back to the source language, and an `OutputParserNode` scores semantic similarity to catch mistranslations
- **Conditional quality branching** — `Workflow.Branch()` routes low-confidence results through a refinement node that provides targeted corrections before the final response
- **Domain terminology enforcement** — domain-specific guidance is injected so legal, medical, technical, finance, marketing, education, and ecommerce vocabulary is rendered consistently
- **Structured output parsing** — `OutputParserNode` extracts typed JSON fields (`translation`, `back_translation`, `quality_score`, `flagged_terms`, `cultural_notes`) from each LLM response into `WorkflowData`
- **Streaming delivery** — the final translation is streamed token-by-token to the Blazor UI via `LlmNode` with `Stream = true`

## Translation Workflow Pipeline

```
POST /api/TranslationApi/translate
        │
        ▼
┌──────────────────────┐
│ 1. FilterNode         │  Validate: source_text non-empty, max 5000 chars
│                       │  target_language must be a supported locale
└──────────┬───────────┘
           ▼
┌──────────────────────┐
│ 2. DetectContext      │  Detect source language and domain
│    (AddStep)          │  → source_language, domain, has_glossary
└──────────┬───────────┘
           ▼
┌──────────────────────┐
│ 3. TranslationPrompt │  PromptBuilderNode
│                       │  Inject source_text, target_language, domain,
│                       │  glossary terms into chain-of-thought template
└──────────┬───────────┘
           ▼
┌──────────────────────┐
│ 4. TranslationLlm    │  LlmNode · Retry=2
│                       │  Reason through idioms and domain terms,
│                       │  produce the final translated text
└──────────┬───────────┘
           ▼
┌──────────────────────┐
│ 5. ParseTranslation  │  OutputParserNode
│                       │  → translation, cultural_notes, flagged_terms
└──────────┬───────────┘
           ▼
┌──────────────────────┐
│ 6. BackTranslation   │  PromptBuilderNode → LlmNode → OutputParserNode
│    Validation         │  Translate result back to source language
│                       │  → back_translation, quality_score (0–100)
└──────────┬───────────┘
           ▼
    ┌───────┴────────────────────────────┐
    │   Branch on quality_score < 75     │
    ▼ true                       false ▼ │
┌──────────────┐        ┌────────────────────────┐
│ 7a. Refine   │        │ 7b. AcceptTranslation   │
│    Translation│        │    (AddStep)            │
│    Prompt +  │        │    Mark result as        │
│    LlmNode + │        │    "accepted" in context│
│    Parser    │        └────────────────────────┘
│    Improve   │
│    flagged   │
│    segments  │
└──────┬───────┘
       ▼
┌──────────────────────┐
│ 8. StreamResponse    │  LlmNode · Stream = true
│                       │  Stream final translated text to browser
│                       │  with quality score and cultural notes
└──────────────────────┘
```

## Key Features

- **Nuanced Translation** — Chain-of-thought prompting reasons through idioms, cultural references, and register before producing output
- **Domain Terminology** — Prompt guidance enforces consistent rendering of legal, medical, technical, finance, marketing, education, and ecommerce terms
- **Back-Translation QA** — Automatic round-trip validation scores semantic fidelity (0–100); results below 75 are automatically refined
- **Cultural Notes** — Each translation includes an optional sidebar of cultural adaptations made during the process
- **Multi-Domain Support** — General, legal, medical, technical, finance, marketing, education, and ecommerce domain modes with tailored prompt guidance
- **Language Detection** — Source language is detected automatically; no manual selection required
- **Streaming UI** — Final translation streams token-by-token into the Blazor editor for instant feedback
- **Flagged Term Review** — Terms with low confidence are surfaced to the user for manual review before accepting

## Project Structure

```
004_Realtime_MultiLingualTranslationHub/
├── Components/
│   ├── Pages/
│   │   ├── Home.razor              # Translation UI — source/target panels, quality badge
│   │   └── Error.razor
│   ├── Layout/
│   │   ├── MainLayout.razor
│   │   └── NavMenu.razor
│   └── App.razor                   # Bootstrap CDN configuration
├── Controllers/
│   └── TranslationApiController.cs # POST /api/TranslationApi/translate
├── wwwroot/                         # Static assets (app.css)
├── Constants.cs                     # All prompt templates
├── Program.cs                       # App / DI configuration
├── appsettings.json                 # Base configuration (committed)
├── appsettings.Development.json
└── appsettings.local.json           # API key overrides (gitignored)
```

## Setup

### 1. Configure API Key

**Option A: Using appsettings.local.json (Recommended)**

Create `appsettings.local.json` in the project root with your OpenAI API key:

```json
{
  "OpenAI": {
    "ApiKey": "sk-your-actual-openai-api-key-here"
  }
}
```

This file is automatically excluded from source control via `.gitignore`.

**Option B: Using environment variables**

```bash
export OpenAI__ApiKey="sk-your-actual-openai-api-key-here"
dotnet run
```

**Security Note:**
- For local development, use `appsettings.local.json` to keep credentials out of source control
- For production, use environment variables or Azure Key Vault
- Never commit API keys to version control

### 2. Run the Application

```bash
dotnet run
```

The application will start at `https://localhost:5001` (or the port shown in the console).

### 3. Translate Text

1. Enter or paste source text in the left panel
2. Select the target language from the dropdown
3. Optionally select a domain (General, Legal, Medical, Technical, Finance, Marketing, Education, E-Commerce)
4. Click **Translate**
5. The translation streams into the right panel with a quality score badge and any cultural notes

## How It Works

### Translation Flow

The core pipeline executes in one HTTP request:

1. **Validation** — rejects empty input or unsupported locales before any LLM call
2. **Context setup** — uses the selected domain (or `general`) and injects matching domain guidance
3. **Chain-of-thought translation** — the LLM is prompted to first identify idioms, cultural references, and domain terms, reason about each, and then produce the final translation
4. **Back-translation validation** — the translated text is independently translated back to the source language; a semantic similarity score is computed
5. **Conditional refinement** — if the quality score falls below 75, a targeted refinement call addresses the flagged low-confidence segments
6. **Streaming delivery** — the accepted translation is streamed to the browser along with quality metadata

### Quality Score

The quality score (0–100) reflects how well the back-translation recovers the original meaning:

| Score | Badge | Meaning |
|---|---|---|
| 90–100 | Green — Excellent | Near-perfect semantic preservation |
| 75–89 | Blue — Good | Minor differences; accepted automatically |
| 50–74 | Yellow — Refined | Routed through the refinement node |
| 0–49 | Red — Review | Surfaced to user for manual review |

### Domain Guidance

Domain guidance is defined in `Constants.cs` under `Constants.GlossaryHints` and is injected into the translation prompt as `{{glossary_hints}}`.

Currently supported domain keys:

- `general`
- `legal`
- `medical`
- `technical`
- `finance`
- `marketing`
- `education`
- `ecommerce`

Example legal guidance excerpt:

```text
Glossary guidance - preserve these legal terms accurately: force majeure,
indemnify/indemnification, jurisdiction, liability, covenant, plaintiff,
defendant, arbitration, breach of contract, due diligence.
```

### API Endpoints

**POST** `/api/TranslationApi/translate`

Request body:
```json
{
  "sourceText": "The contract shall be governed by the laws of the state.",
  "targetLanguage": "es",
  "domain": "legal"
}
```

Response (streamed, then final JSON):
```json
{
  "translation": "El contrato se regirá por las leyes del estado.",
  "sourceLanguage": "en",
  "targetLanguage": "es",
  "domain": "legal",
  "qualityScore": 94,
  "qualityLabel": "Excellent",
  "backTranslation": "The contract will be governed by the laws of the state.",
  "flaggedTerms": [],
  "culturalNotes": "Register maintained as formal legal prose.",
  "timestamp": "2026-04-05T10:30:00Z"
}
```

**GET** `/api/TranslationApi/languages` — returns the list of supported target language codes and display names

## Customization

### Add a New Domain

1. Add a domain guidance constant under `Constants.GlossaryHints` in `Constants.cs`
2. Map the new domain key in `GetGlossaryHints` inside `Controllers/TranslationApiController.cs`
3. Add the domain option in `Components/TranslationWidget.razor`

### Adjust Quality Threshold

Change `QualityRefinementThreshold` in `appsettings.json` (default: `75`) to control when the refinement branch activates.

```json
{
  "Translation": {
    "QualityRefinementThreshold": 75,
    "MaxSourceCharacters": 5000
  }
}
```

