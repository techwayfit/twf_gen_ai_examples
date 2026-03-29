# Domain-Specific Applications — Examples #91–100

Production-grade AI applications for highly specialised domains where domain expertise, regulatory awareness, and responsible AI practices are as important as technical implementation.

---

## Examples

### #91 — Construction Site Safety Compliance Monitor
Analyses construction site photos/video, detects PPE violations, identifies unsafe practices, alerts site managers in real-time, and tracks safety trends.

**Key patterns:** `Workflow.ForEach()` over site images, `HttpRequestNode` for vision API, `OutputParserNode` for violation type + severity, `ConditionNode` for real-time alert routing, `HttpRequestNode` for alert dispatch.

---

### #92 — Judicial Sentencing Consistency Analyzer
Analyses sentencing records across similar cases, identifies statistical disparities, surfaces potential bias patterns, and generates oversight reports.

**Key patterns:** `HttpRequestNode` for legal database API, `EmbeddingNode` for case similarity matching, `Workflow.ForEach()` over comparable cases, `OutputParserNode` for disparity metrics, `LlmNode` for bias pattern narrative.

---

### #93 — Genomic Variant Interpretation Assistant
Interprets variants of uncertain significance (VUS), cross-references ClinVar and literature, and generates clinical interpretation summaries for genetic counsellors.

**Key patterns:** `HttpRequestNode` for ClinVar API + PubMed, `EmbeddingNode` for literature semantic search, `LlmNode` with genomics-domain system prompt, `OutputParserNode` for structured clinical report, `ConditionNode` for actionable-variant flagging.

---

### #94 — Logistics Route Optimizer with Explanations
Evaluates routing options across carriers and modes, considers real-time disruptions, selects optimal routes, and explains recommendations.

**Key patterns:** `Workflow.Parallel()` for multi-carrier API queries, `HttpRequestNode` for weather + port-status APIs, `MergeNode` to combine constraints, `LlmNode` for multi-constraint optimisation reasoning, `OutputParserNode` for ranked routes + explanations.

---

### #95 — Adaptive Game Narrative Engine
Generates dynamic storylines, creates contextually appropriate NPC dialogue, adapts narrative based on player choices, and maintains story consistency.

**Key patterns:** Multi-turn `LlmNode` with `MaintainHistory` for story consistency, `WorkflowContext` global state for player choices and world state, `ConditionNode` for narrative branch selection, `Workflow.Branch()` for diverging storylines.

---

### #96 — Loan Application Underwriting Assistant
Analyses financial documents (bank statements, tax returns), assesses creditworthiness, identifies risk factors, and generates underwriter-ready decision memos.

**Key patterns:** `HttpRequestNode` for document OCR API, `Workflow.Parallel()` for multi-document analysis, `OutputParserNode` for structured financial metrics, `LlmNode` for risk assessment, `ConditionNode` for fair-lending compliance checks.

---

### #97 — Personalized Advertising Copy Generator
Generates personalised ad copy for thousands of audience segments, adapts messaging by demographic and context, and A/B tests variations for conversion optimisation.

**Key patterns:** `Workflow.ForEach()` over audience segments, `PromptBuilderNode` with segment-specific variables, `LlmNode`, `Workflow.Parallel()` for A/B variant generation, `DelayNode.RateLimitDelay()` for API limits.

---

### #98 — Crop Yield Prediction & Farming Advisory
Combines satellite imagery, soil sensor data, and weather forecasts to generate field-specific planting, irrigation, and fertilisation recommendations.

**Key patterns:** `Workflow.Parallel()` for satellite + sensor + weather data fetch, `MergeNode` for multi-source fusion, `LlmNode` with agronomy-domain system prompt, `OutputParserNode` for structured recommendations + confidence intervals.

---

### #99 — Hotel Operations Intelligence System
Analyses guest reviews, monitors service quality, identifies operational issues, generates personalised guest communications, and predicts occupancy trends.

**Key patterns:** `HttpRequestNode` for PMS + review APIs, `Workflow.Parallel()` for sentiment + operational analysis, `OutputParserNode` for structured insights, `PromptBuilderNode` with guest-personalisation variables, `LlmNode` for communication drafting.

---

### #100 — Disaster Response Coordination Assistant
Aggregates situation reports from field teams, social media, and news during a disaster — synthesises the operational picture, prioritises resource needs, and drafts stakeholder communications.

**Key patterns:** `Workflow.Parallel()` for multi-source information aggregation, `MergeNode` for fused situational picture, `LlmNode` for priority-reasoning under uncertainty, `OutputParserNode` for structured sitrep, `ConditionNode` for critical-resource alert routing.

---

## Responsible AI Considerations for This Category

These examples operate in high-stakes domains. When implementing them with TwfAiFramework:

- **Human-in-the-loop:** Use `NodeOptions { ContinueOnError = false }` and `Workflow.Branch()` to route low-confidence outputs to human review rather than auto-acting.
- **Audit trails:** Use `ExecutionTracker` and `WorkflowResult.Report` to log all decisions for regulatory compliance.
- **Confidence gating:** Use `ConditionNode` to threshold on confidence scores before taking consequential actions.
- **Fallback data:** Use `NodeOptions { FallbackData = safeDefault }` to ensure safe defaults when nodes fail.

---

## TwfAiFramework Quickstart for This Category

```csharp
// Typical high-stakes pattern: multi-source gather → analyse → confidence-gate → human review or act
var result = await Workflow.Create("LoanUnderwriting")
    .UseLogger(logger)
    .OnError((node, ex) => alertTeam($"Underwriting node {node} failed: {ex?.Message}"))
    // Fetch all financial documents in parallel
    .Parallel(
        new HttpRequestNode("ParseBankStatement", new HttpRequestConfig
        {
            Method = "POST", UrlTemplate = "https://ocr.example.com/bank-statement"
        }),
        new HttpRequestNode("ParseTaxReturn", new HttpRequestConfig
        {
            Method = "POST", UrlTemplate = "https://ocr.example.com/tax-return"
        })
    )
    // Merge and assess
    .AddNode(new PromptBuilderNode(
        promptTemplate: "Bank statement data: {{bank_statement_data}}\nTax return data: {{tax_return_data}}\n\nAssess creditworthiness and risk.",
        systemTemplate: "You are an experienced loan underwriter. Assess objectively. Return JSON with: risk_score (0-100), risk_factors[], decision (approve/decline/review), confidence (0-1)."))
    .AddNode(new LlmNode(new LlmConfig
    {
        Provider = "openai",
        Model    = "gpt-4o",
        ApiKey   = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!
    }))
    .AddNode(new OutputParserNode(fieldMapping: new()
    {
        ["risk_score"]   = "risk_score",
        ["decision"]     = "underwriting_decision",
        ["confidence"]   = "decision_confidence",
        ["risk_factors"] = "risk_factors"
    }))
    // Route low-confidence decisions to human review
    .AddNode(new ConditionNode("ConfidenceCheck",
        ("needs_human_review", data => data.Get<double>("decision_confidence") < 0.85)))
    .Branch(
        condition:   data => data.Get<bool>("needs_human_review"),
        trueBranch:  b => b.AddNode(new HttpRequestNode("EscalateToReviewer", new HttpRequestConfig
        {
            Method = "POST", UrlTemplate = "https://workflow.example.com/human-review"
        })),
        falseBranch: b => b.AddNode(new HttpRequestNode("AutoDecision", new HttpRequestConfig
        {
            Method = "POST", UrlTemplate = "https://los.example.com/decisions"
        })))
    .RunAsync(new WorkflowData()
        .Set("application_id", "APP-2026-001234"));
```
