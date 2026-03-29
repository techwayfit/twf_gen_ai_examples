# Code & Developer Tools — Examples #36–50

AI-powered developer productivity tools covering code generation, analysis, review, testing, and migration. These examples showcase how to integrate LLMs into software engineering workflows.

---

## Examples

### #36 — AI Pair Programmer with Codebase Context
Indexes an entire codebase, understands architecture and conventions, answers questions, and generates new features consistent with existing patterns.

**Key patterns:** `EmbeddingNode` for codebase indexing, `HttpRequestNode` for vector retrieval, `PromptBuilderNode` with code-context injection, `LlmNode` for generation.

---

### #37 — Automated Bug Explanation & Fix Suggester
Takes error logs, stack traces, and relevant code — identifies root cause, explains the bug, generates fix options, and creates regression tests.

**Key patterns:** `TransformNode` for stack trace parsing, `PromptBuilderNode` with error + code template, `LlmNode`, `OutputParserNode` for fix + test extraction.

---

### #38 — API Documentation Generator
Analyses codebases, extracts public APIs, generates OpenAPI/Swagger specs, writes usage examples, and produces Markdown documentation.

**Key patterns:** `HttpRequestNode` for AST parsing API, `Workflow.ForEach()` over functions, `LlmNode` for docstring + example generation, `MergeNode` for spec assembly.

---

### #39 — Legacy Code Modernizer
Migrates legacy code (COBOL, VBA, Python 2) to modern equivalents, preserves business logic, adds error handling, and writes unit tests.

**Key patterns:** `TransformNode` for code chunking, `LlmNode` for translation per chunk, `Workflow.ForEach()` over modules, `OutputParserNode` for migrated code + test extraction.

---

### #40 — Security Vulnerability Scanner
Scans codebases for OWASP Top 10 vulnerabilities, provides severity ratings, explains exploits, and generates remediation code.

**Key patterns:** `Workflow.ForEach()` over code files, `LlmNode` with security-expert system prompt, `OutputParserNode` for vulnerability classification, `MergeNode` for report.

---

### #41 — Automated Test Case Generator
Analyses source functions, generates comprehensive unit tests (happy path, edge cases, error conditions), and targets coverage goals.

**Key patterns:** `Workflow.ForEach()` over functions, `LlmNode` with test-generation template, `OutputParserNode` for test function extraction, `ConditionNode` for coverage gating.

---

### #42 — System Architecture Advisor
Takes system requirements and proposes architecture options with trade-off analysis, Mermaid diagrams, and bottleneck identification.

**Key patterns:** `OutputParserNode` for requirements decomposition, `Workflow.Parallel()` to generate multiple architecture options, `LlmNode` for trade-off analysis.

---

### #43 — SQL Query Builder from Natural Language
Converts natural language to SQL handling joins, aggregations, window functions, and subqueries — with query explanation and optimisation.

**Key patterns:** `PromptBuilderNode` injecting database schema, `LlmNode` for SQL generation, `OutputParserNode` to extract SQL + explanation, `HttpRequestNode` for query validation.

---

### #44 — Code Review Automator
Analyses pull request diffs for code quality, performance anti-patterns, security issues, and style violations — generates line-by-line PR comments.

**Key patterns:** `HttpRequestNode` for GitHub diff API, `Workflow.ForEach()` over changed files, `OutputParserNode` for structured comment extraction, `HttpRequestNode` for PR comment posting.

---

### #45 — Multi-Language Code Translator
Translates code between languages (Python → TypeScript, Java → Go) while preserving logic and adapting to target idioms.

**Key patterns:** `LlmNode` with language-pair system prompt, `OutputParserNode` for translated code extraction, second `LlmNode` for idiom-review pass.

---

### #46 — Infrastructure-as-Code Generator
Converts plain-English infrastructure requirements to production-ready Terraform / CloudFormation / Pulumi configurations with security best practices.

**Key patterns:** `OutputParserNode` for requirement extraction, `PromptBuilderNode` with IaC-template and security-rules injection, `LlmNode`, `OutputParserNode` for config block extraction.

---

### #47 — Mobile App UI Code Generator
Converts wireframe descriptions or UI sketches to React Native / Flutter code with navigation, state management, and accessibility attributes.

**Key patterns:** `HttpRequestNode` for vision API (image → wireframe description), `PromptBuilderNode` with component-library reference, `LlmNode`, `OutputParserNode` for component extraction.

---

### #48 — Log Analysis & Incident Root Cause Analyzer
Ingests application logs, identifies anomaly patterns, correlates events across services, determines root cause, and generates runbooks.

**Key patterns:** `TransformNode` for log parsing, `Workflow.ForEach()` over log segments, `LlmNode` for correlation reasoning, `OutputParserNode` for structured incident report.

---

### #49 — Design Pattern Recommender
Detects code smells, recommends design patterns, generates refactored examples, and explains reasoning and trade-offs.

**Key patterns:** `LlmNode` with software-architecture system prompt, `OutputParserNode` for pattern taxonomy and refactored-code extraction, `ConditionNode` for smell severity routing.

---

### #50 — CI/CD Pipeline Configuration Generator
Generates complete CI/CD pipeline configs (GitHub Actions, GitLab CI, Jenkins) from project tech stack and deployment requirements.

**Key patterns:** `OutputParserNode` for tech stack extraction, `PromptBuilderNode` with platform-specific YAML templates, `LlmNode`, `OutputParserNode` for YAML block extraction.

---

## TwfAiFramework Quickstart for This Category

```csharp
// Typical pattern: parse input → generate code → extract output
var result = await Workflow.Create("CodeReview")
    .UseLogger(logger)
    // Fetch the PR diff from GitHub
    .AddNode(new HttpRequestNode("GetDiff", new HttpRequestConfig
    {
        Method      = "GET",
        UrlTemplate = "https://api.github.com/repos/{{owner}}/{{repo}}/pulls/{{pr_number}}/files",
        Headers     = new() { ["Authorization"] = "Bearer {{github_token}}" }
    }))
    // Build code review prompt
    .AddNode(new PromptBuilderNode(
        promptTemplate: "Review this code diff for bugs, security issues, and style:\n\n{{http_response}}",
        systemTemplate: "You are a senior software engineer performing a thorough code review. Be specific and actionable."))
    // Run LLM review
    .AddNode(new LlmNode(new LlmConfig
    {
        Provider = "openai",
        Model    = "gpt-4o",
        ApiKey   = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!
    }))
    // Extract structured review comments
    .AddNode(new OutputParserNode(fieldMapping: new()
    {
        ["issues"]      = "review_issues",
        ["suggestions"] = "review_suggestions",
        ["verdict"]     = "review_verdict"
    }))
    .RunAsync(new WorkflowData()
        .Set("owner", "my-org")
        .Set("repo", "my-repo")
        .Set("pr_number", "42")
        .Set("github_token", Environment.GetEnvironmentVariable("GITHUB_TOKEN")!));
```
