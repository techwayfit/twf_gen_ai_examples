# 002 - AI Email Drafting Assistant

## Project Overview

This example builds an AI-powered customer support inbox for **TechWayFit**, a fictional fitness technology company, using ASP.NET Core Blazor Server and the **TwfAiFramework**. Support agents can read incoming emails, inspect thread history, and generate context-aware reply drafts with a single click — with the AI response streamed token-by-token directly into the editor.

The reply drafting pipeline is a **seven-node TwfAiFramework workflow** that performs sentiment analysis and conditional thread summarisation before assembling an enriched prompt for the final streaming LLM call.

## Objective

Demonstrate how to build a production-grade AI writing assistant that goes beyond a simple "call the LLM" approach:

- **Multi-node analysis pipeline** — chain validation, data gathering, sentiment analysis, conditional thread summarisation, prompt assembly, and streaming generation into a single readable workflow
- **Conditional branching** — use `Workflow.Branch()` to run thread summarisation only when thread history actually exists, skipping it otherwise
- **Structured LLM output parsing** — use `OutputParserNode` to extract typed JSON fields (`sentiment`, `urgency`, `key_issues`) from intermediate LLM responses into `WorkflowData`
- **Framework-native streaming** — use `LlmNode` with `Stream = true` and `OnChunk = Action<string>` (TwfAiFramework 1.0.1) to forward tokens directly to the SSE response, eliminating custom HTTP streaming code
- **Context-aware prompts** — feed sentiment, urgency, key issues, thread summary, and sender history into the final prompt so the LLM produces a genuinely informed reply
- **Email service layer** — a CSV-backed `EmailService` that serves threads, sender history, and full-text search without any database
- **Tone selection** — the agent picks a tone (Professional, Friendly, Empathetic, Formal, Concise) before generating, which is wired into the final prompt instruction

## Reply Workflow Pipeline

```
POST /api/AIService/draft-reply
        │
        ▼
┌─────────────────────┐
│ 1. FilterNode        │  Validate: message_id must be non-empty
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 2. GatherEmailData  │  Load email, thread messages, sender history
│    (AddStep)        │  → email_body, thread_messages, sender_history,
└──────────┬──────────┘    has_thread, sender_name, subject, date …
           ▼
┌─────────────────────┐
│ 3. SentimentAnalysis│  PromptBuilderNode ({{email_body}})
│    Prompt + LlmNode │  → JSON: { sentiment, urgency, key_issues }
│    + OutputParser   │
└──────────┬──────────┘
           ▼
    ┌──────┴────────────────────────┐
    │   Branch on has_thread        │
    ▼ true                  false ▼ │
┌──────────────┐    ┌────────────────────┐
│ 4a. Thread   │    │ 4b. NoThreadContext │
│    Analysis  │    │    (AddStep)        │
│    Prompt +  │    │    → thread_summary │
│    LlmNode + │    │       = "None"      │
│    Parser    │    └─────────────────────┘
└──────┬───────┘
       │ → thread_summary, ongoing_issues
       ▼
┌─────────────────────┐
│ 5. BuildFinalPrompt │  Assemble enriched prompt from all gathered fields
│    (AddStep)        │  (sentiment, urgency, key_issues, thread_summary,
└──────────┬──────────┘   ongoing_issues, sender_history, tone)
           ▼
┌─────────────────────┐
│ 6. FinalReplyPrompt │  PromptBuilderNode.WithSystem
│    PromptBuilder    │  passes {{final_user_prompt}} + system persona
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 7. FinalReplyStream │  LlmNode { Stream = true, OnChunk = sseWriter }
│    LlmNode          │  Tokens stream to UI via Server-Sent Events
└─────────────────────┘
```

## Key Features

| Feature | Detail |
|---|---|
| **AI Reply Drafting** | 7-node workflow generates context-aware replies with full thread and sender awareness |
| **Streaming responses** | Tokens delivered to the browser in real time via SSE using `LlmNode.OnChunk` |
| **Sentiment analysis** | Detects `positive / neutral / frustrated / angry` and `low / medium / high / critical` urgency before drafting |
| **Thread summarisation** | Conditional branch summarises prior thread messages so the reply doesn't repeat resolved issues |
| **Sender history** | Fetches the customer's last 4 emails for broader context |
| **Tone selector** | Agent selects Professional, Friendly, Empathetic, Formal, or Concise before generating |
| **Edit & Send flow** | AI draft drops into the standard reply panel for agent review before sending |
| **Inbox UI** | Dual-panel Blazor Server inbox with search, unread filter, thread view, and mobile layout |
| **CSV email data** | `EmailService` backed by `techwayfit_emails.csv` — no database required |
| **Email analysis service** | Separate `EmailAnalysisService` uses the Anthropic API to classify ticket type, sentiment, and urgency |
| **Retry resilience** | `NodeOptions.WithRetry(2)` on all intermediate LLM nodes |

## Project Structure

```
002_EmailDraftingAssistant/
├── Components/
│   ├── Pages/
│   │   └── Inbox.razor          # Dual-panel inbox UI, AI reply panel, SSE streaming client
│   ├── Layout/
│   │   ├── MainLayout.razor
│   │   └── NavMenu.razor
│   └── App.razor
├── Controllers/
│   └── AIServiceController.cs   # POST /api/AIService/draft-reply (streaming workflow)
│                                # GET  /api/AIService/health
├── Services/
│   ├── CsvEmailReader.cs        # Parses techwayfit_emails.csv into EmailMessage list
│   ├── EmailService.cs          # Raw queries: GetById, GetThread, GetBySender, Search
│   ├── EmailAnalysisService.cs  # Anthropic API — TicketType, Sentiment, Urgency analysis
│   ├── EmailMessage.cs          # Core email model
│   └── EmailModels.cs           # TicketType, UrgencyLevel, Sentiment enums + analysis models
├── Data/
│   └── techwayfit_emails.csv    # Sample inbox data (150+ emails across threads)
├── Constants.cs                 # All prompt templates for sentiment, thread, and reply nodes
├── Program.cs                   # DI, Kestrel AllowSynchronousIO, service registration
├── appsettings.json             # Base configuration (committed)
└── appsettings.local.json       # API key overrides (gitignored)
```

## Setup

### 1. Configure the OpenAI API Key

Create `appsettings.local.json` in the project root:

```json
{
  "OpenAI": {
    "ApiKey": "sk-your-openai-api-key-here",
    "Model": "gpt-4o-mini",
    "Endpoint": "https://api.openai.com/v1/chat/completions"
  }
}
```

`Model` and `Endpoint` default to `gpt-4o-mini` / OpenAI if omitted — override to use Azure OpenAI, Ollama, or any OpenAI-compatible endpoint.

> **Security note:** `appsettings.local.json` is gitignored. Never commit API keys to source control. Use environment variables or a secrets manager in production.

### 2. Run the Application

```bash
dotnet run
# or for hot-reload during development:
dotnet watch
```

The app starts at `https://localhost:5001` (port shown in the console).

### 3. Use the Inbox

1. Open the **Inbox** page
2. Click any email to open it in the detail panel
3. Click **Reply with AI** in the message header
4. Select a **tone** (Professional, Friendly, Empathetic, Formal, or Concise)
5. Click **Generate Draft** — the reply streams in token-by-token
6. Review the generated text, then click **Edit & Send** to move it to the reply panel

## API Endpoints

| Method | Route | Description |
|---|---|---|
| `POST` | `/api/AIService/draft-reply` | Streams an AI reply draft via Server-Sent Events |
| `GET` | `/api/AIService/health` | Health check |

### `POST /api/AIService/draft-reply` request body

```json
{
  "messageId": "MSG-001",
  "tone": "empathetic"
}
```

### SSE response format

Each token is delivered as an SSE event:

```
data: {"delta":"Thank"}

data: {"delta":" you"}

data: {"delta":" for"}

data: [DONE]
```

Errors are returned as:

```
data: {"error":"Email 'MSG-999' not found."}
```

## TwfAiFramework Patterns Demonstrated

| Pattern | Where used |
|---|---|
| `FilterNode` | Input validation — blocks empty `message_id` before any LLM call |
| `AddStep` lambda | `GatherEmailData` and `BuildFinalPrompt` — pure data loading and assembly with no LLM overhead |
| `PromptBuilderNode` (constructor) | Sentiment and thread analysis prompts with `{{key}}` interpolation from `WorkflowData` |
| `PromptBuilderNode.WithSystem` | Final reply prompt with system persona and user message |
| `LlmNode` | Sentiment analyser, thread analyser, and streaming reply generator |
| `LlmNode` with `Stream = true` / `OnChunk` | Token-level streaming — framework owns the SSE loop; no custom HTTP streaming code |
| `OutputParserNode.WithMapping` | Parse LLM JSON output (`sentiment`, `urgency`, `key_issues`, `thread_summary`, `ongoing_issues`) into typed `WorkflowData` keys |
| `Workflow.Branch` | Skip thread analysis entirely when no thread history exists |
| `NodeOptions.WithRetry(n)` | Transient failure recovery on all intermediate LLM nodes |
