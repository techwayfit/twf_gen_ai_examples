# Document Intelligence — Examples #21–35

RAG-based and document-processing applications that extract, synthesise, and analyse information from multi-format documents. Master vector search, chunking, and structured extraction here.

---

## Examples

### #21 — Multi-Document Research Synthesizer
Ingests 50+ research papers into a semantic index and answers cross-cutting questions with citations, highlighting agreements and contradictions.

**Key patterns:** `EmbeddingNode` for indexing, `HttpRequestNode` for vector store retrieval, `PromptBuilderNode` injecting retrieved chunks, `OutputParserNode` for citation extraction.

---

### #22 — Financial Report Analyzer
Processes annual reports (10-K, 10-Q), extracts financial metrics, compares year-over-year trends, and answers natural language queries.

**Key patterns:** `TransformNode` for PDF table extraction, `Workflow.ForEach()` over report sections, `LlmNode` for numerical reasoning, `OutputParserNode` for structured financials.

---

### #23 — Legal Case Research Assistant
Searches case law, summarises holdings and reasoning, identifies applicable statutes, and drafts research memos with citations.

**Key patterns:** `HttpRequestNode` for legal database APIs, `EmbeddingNode` for semantic retrieval, `LlmNode` for summarisation, `PromptBuilderNode` with citation-format templates.

---

### #24 — Medical Records Summarizer
Generates structured clinical summaries (SOAP format), identifies care gaps, and prepares pre-visit briefings for physicians.

**Key patterns:** `TransformNode` for medical record segmentation, `OutputParserNode` with `fieldMapping` for SOAP field extraction, `ConditionNode` for care gap flagging.

---

### #25 — Resume Parser & Candidate Ranker
Parses resumes in any format, scores candidates against job requirements semantically, and generates interview question suggestions.

**Key patterns:** `HttpRequestNode` for document parsing API, `EmbeddingNode` for semantic scoring, `Workflow.ForEach()` over candidates, `OutputParserNode` for structured profiles.

---

### #26 — Contract Comparison Engine
Compares two contract versions, highlights differences, categorises changes by risk level, and generates a negotiation brief.

**Key patterns:** `TransformNode` for diff extraction, `Workflow.ForEach()` over changed sections, `OutputParserNode` for risk classification, `MergeNode` for final brief assembly.

---

### #27 — Scientific Literature Review Generator
Retrieves recent papers from PubMed/arXiv, synthesises findings, identifies research trends, and generates a structured literature review.

**Key patterns:** `HttpRequestNode` for academic API calls, `EmbeddingNode` + `Workflow.Parallel()` for concurrent paper embedding, `LlmNode` for synthesis, `OutputParserNode` for trend extraction.

---

### #28 — Invoice & Receipt Data Extractor
Extracts structured data from invoices (vendor, amount, line items, tax), validates extractions, and integrates with accounting software.

**Key patterns:** `HttpRequestNode` for vision/OCR API, `OutputParserNode` with strict JSON schema, `ConditionNode` for anomaly flagging, `HttpRequestNode` for accounting API sync.

---

### #29 — Earnings Call Sentiment Analyzer
Analyses earnings transcripts, extracts management guidance, measures executive sentiment, and generates investor-facing summaries.

**Key patterns:** `TransformNode` for transcript segmentation, `Workflow.Parallel()` for per-speaker analysis, `OutputParserNode` for bullish/bearish signal extraction.

---

### #30 — RFP Response Generator
Extracts RFP requirements, maps against a capability database, drafts responses, and assembles a compliant submission document.

**Key patterns:** `OutputParserNode` for requirements extraction, `EmbeddingNode` for capability matching, `Workflow.ForEach()` over requirements, `MergeNode` for document assembly.

---

### #31 — Regulatory Compliance Checker
Checks business documents against regulatory requirements (GDPR, HIPAA, SOC 2), flags non-compliant language, and generates compliance gap reports.

**Key patterns:** `Workflow.ForEach()` over document sections, `LlmNode` with regulation-injected prompts, `OutputParserNode` for compliance classification, `MergeNode` for gap report.

---

### #32 — Textbook Chapter Question Generator
Generates MCQs, short answers, essays, and true/false statements calibrated to Bloom's Taxonomy levels from textbook content.

**Key patterns:** `Workflow.Parallel()` for simultaneously generating different question types, `OutputParserNode` for structured question-answer-distractor extraction.

---

### #33 — Policy Document Navigator
Answers employee policy questions with precise citations, detects policy conflicts, and identifies outdated policies.

**Key patterns:** `EmbeddingNode` for policy indexing, `HttpRequestNode` for vector retrieval, `OutputParserNode` for citation extraction, `ConditionNode` for conflict detection.

---

### #34 — Patent Analysis & Prior Art Search Tool
Extracts patent claims, searches prior art databases, assesses novelty, and generates a freedom-to-operate summary.

**Key patterns:** `OutputParserNode` for claim extraction, `HttpRequestNode` for patent database API, `EmbeddingNode` for semantic prior-art search, `LlmNode` for FTO analysis.

---

### #35 — Clinical Trial Protocol Analyzer
Parses eligibility criteria, matches patients from databases, detects protocol deviations, and generates regulatory summaries.

**Key patterns:** `OutputParserNode` for criteria parsing, `HttpRequestNode` for patient database queries, `ConditionNode` for eligibility evaluation, `Workflow.ForEach()` over patients.

---

## TwfAiFramework Quickstart for This Category

```csharp
// Typical RAG pattern: Embed query → Retrieve → Inject → LLM → Parse
var result = await Workflow.Create("DocIntelligence")
    .UseLogger(logger)
    // 1. Embed the user query
    .AddNode(new EmbeddingNode(new EmbeddingConfig
    {
        Model  = "text-embedding-3-small",
        ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!
    }))
    // 2. Retrieve relevant chunks from vector store
    .AddNode(new HttpRequestNode("VectorSearch", new HttpRequestConfig
    {
        Method      = "POST",
        UrlTemplate = "https://your-vector-store/query",
        Body        = new { top_k = 5 }
    }))
    // 3. Build grounded prompt with retrieved context
    .AddNode(new PromptBuilderNode(
        promptTemplate: "Context:\n{{http_response}}\n\nQuestion: {{question}}",
        systemTemplate: "Answer only based on the provided context. Cite sources."))
    // 4. Call LLM
    .AddNode(new LlmNode(new LlmConfig
    {
        Provider = "openai",
        Model    = "gpt-4o",
        ApiKey   = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!
    }))
    // 5. Extract structured answer + citations
    .AddNode(new OutputParserNode(fieldMapping: new()
    {
        ["answer"]    = "answer",
        ["citations"] = "citations"
    }))
    .RunAsync(new WorkflowData()
        .Set("question", "What were the key risk factors in the 2024 annual report?")
        .Set("text", "key risk factors analysis 2024 annual report"));
```
