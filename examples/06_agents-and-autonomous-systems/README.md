# Agents & Autonomous Systems — Examples #69–80

Autonomous multi-step agents that plan, execute, and adapt without human intervention. These examples demonstrate advanced TwfAiFramework patterns: branching, loops, parallel execution, and error recovery.

---

## Examples

### #69 — Autonomous Research Agent
Accepts a research question, plans a multi-step investigation, executes web searches, reads papers, and produces a comprehensive report — all without human intervention.

**Key patterns:** `Workflow.ForEach()` over search results, `HttpRequestNode` for web search + paper fetch, `EmbeddingNode` for paper indexing, `LlmNode` for synthesis, `Workflow.Branch()` for gap-detection and follow-up searches.

---

### #70 — E-Commerce Shopping Agent
Searches multiple sites, compares specs and prices, reads reviews, filters by preferences, and presents a ranked recommendation with reasoning.

**Key patterns:** `Workflow.Parallel()` for simultaneous multi-site queries, `HttpRequestNode` per retailer API, `LlmNode` for comparative analysis, `OutputParserNode` for ranked recommendation.

---

### #71 — Autonomous Data Analyst Agent
Connects to databases, interprets business questions, writes and executes SQL/Python, generates visualisations, and creates executive summary reports.

**Key patterns:** `LlmNode` for SQL generation, `HttpRequestNode` for database query execution, `ConditionNode` for error-recovery (retry with corrected query), `LlmNode` for narrative generation.

---

### #72 — Personal Finance Management Agent
Categorises transactions, identifies spending patterns, tracks budget goals, finds savings opportunities, and proactively alerts to financial risks.

**Key patterns:** `HttpRequestNode` for Plaid API, `Workflow.ForEach()` over transactions, `OutputParserNode` for categorisation, `ConditionNode` + `Workflow.Branch()` for anomaly alerting.

---

### #73 — Autonomous Calendar & Scheduling Optimizer
Analyses calendars, negotiates meeting times via email, blocks focus time, reschedules conflicts, and optimises daily schedules for productivity.

**Key patterns:** `HttpRequestNode` for Calendar API, `LlmNode` for constraint reasoning, `Workflow.ForEach()` over scheduling proposals, `ConditionNode` for conflict detection, `HttpRequestNode` for email dispatch.

---

### #74 — Autonomous Inventory Management Agent
Monitors inventory levels, predicts reorder points, generates purchase orders, and coordinates with suppliers via automated emails.

**Key patterns:** `HttpRequestNode` for inventory API, `LlmNode` for demand forecasting reasoning, `ConditionNode` for reorder-threshold detection, `Workflow.Branch()` for PO generation vs. monitoring continuation.

---

### #75 — Automated A/B Testing Agent
Designs A/B tests from business hypotheses, monitors results in real-time, determines statistical significance, and recommends ship/rollback decisions.

**Key patterns:** `HttpRequestNode` for analytics platform API, `LlmNode` for statistical reasoning, `ConditionNode` for significance threshold, `Workflow.Branch()` for ship vs. rollback recommendation.

---

### #76 — Web Scraping & Competitive Intelligence Agent
Monitors competitor websites, detects changes, analyses competitive positioning shifts, and generates weekly intelligence briefings.

**Key patterns:** `HttpRequestNode` for competitor page fetching, `EmbeddingNode` for change detection, `Workflow.Parallel()` for multi-competitor monitoring, `LlmNode` for briefing synthesis.

---

### #77 — DevOps Automation Agent
Monitors system health, responds to alerts, diagnoses issues from logs and metrics, executes remediation playbooks, and files incident reports.

**Key patterns:** `HttpRequestNode` for cloud monitoring APIs, `LlmNode` for root cause reasoning, `ConditionNode` for severity routing, `Workflow.Branch()` for auto-remediate vs. escalate, `HttpRequestNode` for remediation actions.

---

### #78 — Marketing Campaign Orchestration Agent
Plans multi-channel campaigns, generates creative assets, schedules publication, monitors performance, and makes real-time budget adjustments.

**Key patterns:** `Workflow.Parallel()` for simultaneous creative generation across channels, `HttpRequestNode` for ad platform APIs, `ConditionNode` for performance-budget triggering, `LlmNode` for creative generation.

---

### #79 — Healthcare Appointment Coordinator Agent
Manages appointment scheduling, sends reminders, handles cancellations, and coordinates care between multiple providers.

**Key patterns:** `HttpRequestNode` for EHR/calendar APIs, `LlmNode` for patient communication drafting, `ConditionNode` for no-show risk detection, `Workflow.Branch()` for reminder escalation.

---

### #80 — Autonomous Fraud Investigation Agent
Gathers evidence from transaction systems and social data, analyses patterns, assigns fraud probability scores, and recommends account actions.

**Key patterns:** `Workflow.Parallel()` for multi-source evidence gathering, `LlmNode` for pattern reasoning, `OutputParserNode` for fraud score extraction, `ConditionNode` + `Workflow.Branch()` for escalation routing.

---

## TwfAiFramework Quickstart for This Category

```csharp
// Typical agentic pattern: plan → execute loop → branch on result → synthesise
var result = await Workflow.Create("ResearchAgent")
    .UseLogger(logger)
    // Step 1: Generate a search plan
    .AddNode(new PromptBuilderNode(
        promptTemplate: "Create a 3-step research plan for: {{research_question}}",
        systemTemplate: "You are a research strategist. Return JSON with steps[]."))
    .AddNode(new LlmNode(config))
    .AddNode(new OutputParserNode(fieldMapping: new() { ["steps"] = "research_steps" }))
    // Step 2: Execute each search step
    .ForEach("research_steps", "search_results", body => body
        .AddNode(new PromptBuilderNode("Search query for: {{item}}"))
        .AddNode(new LlmNode(config))  // generates optimised search query
        .AddNode(new HttpRequestNode("WebSearch", new HttpRequestConfig
        {
            Method      = "GET",
            UrlTemplate = "https://api.search.example.com?q={{llm_response}}"
        })))
    // Step 3: Check if more research needed
    .AddNode(new ConditionNode("CheckCompleteness",
        ("needs_more_research", data => data.Get<List<object>>("search_results")?.Count < 3)))
    .Branch(
        condition:   data => data.Get<bool>("needs_more_research"),
        trueBranch:  b => b.AddNode(new HttpRequestNode("DeepSearch", /* fallback search */ null!)),
        falseBranch: null)
    // Step 4: Synthesise final report
    .AddNode(new PromptBuilderNode(
        promptTemplate: "Research question: {{research_question}}\n\nSources:\n{{search_results}}\n\nWrite a comprehensive report.",
        systemTemplate: "You are an expert analyst. Synthesise findings with citations."))
    .AddNode(new LlmNode(config))
    .RunAsync(new WorkflowData()
        .Set("research_question", "What are the emerging trends in edge AI for 2026?"));
```
