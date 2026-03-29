# Data, Analytics & Search — Examples #81–90

AI-powered analytics, intelligent search, and data intelligence applications. These examples demonstrate combining structured data sources with LLM reasoning to surface insights and drive decisions.

---

## Examples

### #81 — Semantic Enterprise Search Engine
Indexes internal documents, wikis, and databases — understands semantic context, ranks by relevance and freshness, and provides AI-generated answer summaries with citations.

**Key patterns:** `EmbeddingNode` for document indexing, `HttpRequestNode` for hybrid search (BM25 + vector), `PromptBuilderNode` with retrieved chunks, `LlmNode` for answer generation, `OutputParserNode` for citation extraction.

---

### #82 — Business KPI Anomaly Detective
Monitors KPI dashboards, detects statistical anomalies, investigates contributing factors by querying related data sources, and generates natural-language explanations.

**Key patterns:** `HttpRequestNode` for metrics API, `ConditionNode` for anomaly threshold detection, `Workflow.Branch()` for investigation sub-workflow, `LlmNode` for causal explanation generation.

---

### #83 — Real-Time News Trend Analyzer
Monitors global news sources, identifies emerging trends, clusters related stories, tracks narrative evolution, and generates trend briefings.

**Key patterns:** `HttpRequestNode` for news API, `EmbeddingNode` for topic clustering, `Workflow.ForEach()` over trend clusters, `LlmNode` for briefing synthesis, `OutputParserNode` for structured trend report.

---

### #84 — Student Performance Prediction System
Ingests student engagement data, identifies at-risk students early, predicts outcomes, and suggests personalised interventions.

**Key patterns:** `HttpRequestNode` for LMS API, `LlmNode` for risk-factor analysis, `OutputParserNode` for risk score + intervention extraction, `ConditionNode` for alert threshold routing.

---

### #85 — Algorithmic Trading Signal Generator
Analyses market data, financial news, and social sentiment — generates trading signals with confidence scores and risk-adjusted position sizing.

**Key patterns:** `Workflow.Parallel()` for simultaneous market data + news + sentiment fetch, `MergeNode` to combine signals, `LlmNode` for signal synthesis, `OutputParserNode` for structured signal + confidence.

---

### #86 — Urban Planning Insights Generator
Analyses urban datasets (traffic, demographics, zoning, services), generates livability insights, identifies underserved areas, and models development impact.

**Key patterns:** `Workflow.Parallel()` for multi-dataset API fetches, `MergeNode` for data fusion, `LlmNode` for policy impact analysis, `OutputParserNode` for structured planning report.

---

### #87 — Customer Churn Predictor & Retention Advisor
Predicts churn probability, identifies risk factors, generates personalised retention offers, and drafts outreach messages.

**Key patterns:** `HttpRequestNode` for CRM API, `LlmNode` for risk-factor analysis, `Workflow.ForEach()` over at-risk customers, `PromptBuilderNode` with personalisation variables, `LlmNode` for outreach drafting.

---

### #88 — Climate Data Storyteller
Ingests climate datasets, generates accessible narratives for different audiences (public, policymakers, scientists), and answers data questions.

**Key patterns:** `HttpRequestNode` for climate data API, `ConditionNode` for audience-type routing, `Workflow.Branch()` for audience-specific `PromptBuilderNode` templates, `LlmNode` for narrative generation.

---

### #89 — Sports Performance Analytics Narrator
Processes game statistics and tracking data, generates coach-ready performance reports, identifies tactical patterns, and creates fan-friendly summaries.

**Key patterns:** `HttpRequestNode` for sports data API, `Workflow.Parallel()` for coach-report + fan-summary generation, `OutputParserNode` for structured insights, `ConditionNode` for audience routing.

---

### #90 — Knowledge Graph Builder from Unstructured Text
Extracts entities, relationships, and events from unstructured text — builds a queryable knowledge graph with natural language query capabilities.

**Key patterns:** `Workflow.ForEach()` over text chunks, `LlmNode` for entity + relationship extraction, `OutputParserNode` for structured triples, `HttpRequestNode` for Neo4j/graph database API to persist graph.

---

## TwfAiFramework Quickstart for This Category

```csharp
// Typical pattern: fetch multi-source data → merge → analyse → generate insight
var result = await Workflow.Create("KPIAnomalyDetector")
    .UseLogger(logger)
    // Fetch current metrics and historical baseline in parallel
    .Parallel(
        new HttpRequestNode("CurrentMetrics", new HttpRequestConfig
        {
            Method      = "GET",
            UrlTemplate = "https://analytics.example.com/kpis/current"
        }),
        new HttpRequestNode("HistoricalBaseline", new HttpRequestConfig
        {
            Method      = "GET",
            UrlTemplate = "https://analytics.example.com/kpis/baseline"
        })
    )
    // Flag anomalies
    .AddNode(new ConditionNode("DetectAnomalies",
        ("has_anomaly", data =>
        {
            var current  = data.Get<double>("current_revenue") ?? 0;
            var baseline = data.Get<double>("baseline_revenue") ?? 1;
            return Math.Abs((current - baseline) / baseline) > 0.15; // >15% deviation
        })))
    // Only investigate if anomaly detected
    .Branch(
        condition:  data => data.Get<bool>("has_anomaly"),
        trueBranch: b => b
            .AddNode(new PromptBuilderNode(
                promptTemplate: "Current KPIs: {{http_response}}\nBaseline: {{baseline_revenue}}\n\nInvestigate the anomaly and explain root causes.",
                systemTemplate: "You are a business analyst. Provide structured root cause analysis."))
            .AddNode(new LlmNode(new LlmConfig
            {
                Provider = "openai",
                Model    = "gpt-4o",
                ApiKey   = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!
            }))
            .AddNode(new OutputParserNode(fieldMapping: new()
            {
                ["root_causes"]       = "anomaly_causes",
                ["recommendations"]   = "anomaly_recommendations",
                ["severity"]          = "anomaly_severity"
            })))
    .RunAsync(new WorkflowData());
```
