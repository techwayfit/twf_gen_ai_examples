namespace _030_RFPComplianceEngine;

public static class Constants
{
    public static class Prompts
    {
        // ── Requirements extraction (#30) ────────────────────────────────────

        public const string RequirementsExtractionSystemPrompt =
            "You are an expert RFP analyst. Parse the provided Request for Proposal document and extract " +
            "all discrete requirements. Each requirement should have a unique ID, a clear description, a category " +
            "(security, data_handling, compliance, technical, operational, financial), a priority level " +
            "(critical, high, medium, low), and a list of applicable compliance frameworks " +
            "(e.g. GDPR, HIPAA, SOC2, FDA). Use only information present in the RFP; do not introduce " +
            "external requirements. Return only valid JSON with no markdown code fences or extra commentary.";

        public const string RequirementsExtractionPrompt =
            @"RFP document text:
{{rfp_text}}

Extract all requirements from this RFP. Return ONLY valid JSON (no markdown, no code fences):
{
  ""requirements"": [
    {
      ""id"": ""REQ-001"",
      ""description"": ""Vendor must demonstrate SOC 2 Type II compliance for all data processing operations."",
      ""category"": ""compliance"",
      ""priority"": ""critical"",
      ""compliance_frameworks"": [""SOC2""]
    }
  ]
}

Quality requirements:
- Extract 5-30 concrete, actionable requirements.
- Each requirement must be specific and testable, not vague statements.
- Assign each requirement to exactly one category.
- Map each requirement to applicable compliance frameworks.
- Preserve the original wording as much as possible while making requirements discrete.";

        // ── Capability matching (#30) ────────────────────────────────────────

        public const string CapabilityMatchingSystemPrompt =
            "You are a proposal response specialist. Given an RFP requirement and relevant company capabilities " +
            "(retrieved from the capability database), draft a concise response that directly addresses the " +
            "requirement. Cite specific capabilities, certifications, or past performance. If the company cannot " +
            "fully meet the requirement, state what can be provided and what gaps remain. " +
            "Return only valid JSON with no markdown code fences.";

        public const string CapabilityMatchingPrompt =
            @"RFP Requirement:
{{requirement}}

Company capabilities retrieved from database:
{{capabilities_context}}

Draft a response to this requirement. Return ONLY valid JSON (no markdown, no code fences):
{
  ""requirement_id"": ""REQ-001"",
  ""response"": ""Your drafted response addressing the requirement..."",
  ""capability_matches"": [""Cap-001"", ""Cap-002""],
  ""confidence"": ""high"",
  ""gaps"": ""Any gaps or limitations in meeting this requirement""
}

Quality requirements:
- Directly address the requirement — do not deflect or pad.
- Cite specific capabilities, certifications, or case studies.
- Use a professional, confident tone.
- If gaps exist, be transparent and propose mitigation.";

        // ── Compliance checking (#31) ────────────────────────────────────────

        public const string ComplianceCheckSystemPrompt =
            "You are a regulatory compliance expert specializing in {{frameworks}}. Analyze the provided " +
            "RFP requirement and company response against each applicable regulatory framework. Flag any " +
            "non-compliant language, missing mandatory controls, or insufficient evidence of compliance. " +
            "For each issue found, provide the specific regulation clause, the non-compliant text, a risk level " +
            "(critical, high, medium, low), and a recommended fix. " +
            "Return only valid JSON with no markdown code fences.";

        public const string ComplianceCheckPrompt =
            @"RFP Requirement:
{{requirement}}

Company Response:
{{response}}

Applicable compliance frameworks: {{frameworks}}

Regulatory context retrieved from the regulations database:
{{regulations_context}}

Check this response against each applicable framework. Return ONLY valid JSON (no markdown, no code fences):
{
  ""requirement_id"": ""REQ-001"",
  ""overall_compliance"": ""partial"",
  ""findings"": [
    {
      ""framework"": ""GDPR"",
      ""clause"": ""Article 28 - Processor obligations"",
      ""status"": ""non_compliant"",
      ""issue"": ""Response does not mention Data Processing Agreement requirements"",
      ""risk_level"": ""high"",
      ""recommendation"": ""Include specific mention of DPA execution and subprocessor obligations""
    }
  ],
  ""summary"": ""Brief overall compliance summary""
}

Quality requirements:
- Check against each applicable framework separately.
- Reference specific articles, clauses, or control objectives.
- Be precise about what is missing or non-compliant.
- Provide actionable recommendations.";

        // ── Policy alignment (#33) ───────────────────────────────────────────

        public const string PolicyAlignmentSystemPrompt =
            "You are an internal policy advisor. Given an RFP requirement and relevant company policy " +
            "documents, assess whether the proposed response aligns with internal policies. " +
            "Cite specific policy sections. Flag any policy conflicts or outdated language. " +
            "If policies conflict with each other, identify the conflict. " +
            "Return only valid JSON with no markdown code fences.";

        public const string PolicyAlignmentPrompt =
            @"RFP Requirement:
{{requirement}}

Company Response:
{{response}}

Relevant internal policies retrieved from the policy database:
{{policies_context}}

Assess alignment with internal policies. Return ONLY valid JSON (no markdown, no code fences):
{
  ""requirement_id"": ""REQ-001"",
  ""alignment"": ""aligned"",
  ""citations"": [
    {
      ""policy_id"": ""POL-001"",
      ""policy_title"": ""Data Handling Policy"",
      ""section"": ""3.2"",
      ""excerpt"": ""All third-party processors must undergo annual security review..."",
      ""alignment"": ""aligned""
    }
  ],
  ""conflicts"": [],
  ""recommendations"": ""Any policy-based changes needed""
}

Quality requirements:
- Cite specific policy IDs, titles, and section numbers.
- Include relevant excerpts that support or contradict the response.
- Flag outdated policies that haven't been reviewed in 12+ months.
- Identify inter-policy conflicts when they exist.";

        // ── Policy conflict detection (#33) ──────────────────────────────────

        public const string PolicyConflictSystemPrompt =
            "You are an internal policy auditor. Given a list of company policies with their alignment " +
            "assessments, detect any conflicts between policies, identify outdated policies, and recommend " +
            "updates. Return only valid JSON with no markdown code fences.";

        public const string PolicyConflictPrompt =
            @"Policy alignment results across all RFP requirements:
{{policy_alignment_summary}}

Analyze these policies for:
1. Inter-policy conflicts (two policies that contradict each other)
2. Outdated policies (not reviewed/updated in 12+ months)
3. Gaps (requirements not covered by any existing policy)

Return ONLY valid JSON (no markdown, no code fences):
{
  ""conflicts"": [
    {
      ""policy_a"": ""POL-001"",
      ""policy_b"": ""POL-003"",
      ""description"": ""Data retention policy contradicts data minimization principle in privacy policy"",
      ""severity"": ""high""
    }
  ],
  ""outdated_policies"": [
    {
      ""policy_id"": ""POL-002"",
      ""policy_title"": ""Access Control Policy"",
      ""last_reviewed"": ""2024-01-15"",
      ""recommendation"": ""Update to reflect current zero-trust architecture""
    }
  ],
  ""coverage_gaps"": [
    ""No policy addresses AI/ML data governance requirements""
  ]
}";

        // ── Response drafting (#30) ──────────────────────────────────────────

        public const string ResponseDraftingSystemPrompt =
            "You are a professional proposal writer. Given all the per-requirement responses, compliance " +
            "findings, and policy alignments, assemble a polished, professional RFP response document. " +
            "Organize by requirement category. Include compliance evidence where applicable. " +
            "Format as clean Markdown with headings, tables, and professional language. " +
            "Do not include internal notes or gap information — only the client-facing response.";

        public const string ResponseDraftingPrompt =
            @"Per-requirement analysis:
{{requirement_results}}

Draft a professional RFP response document. Return ONLY valid JSON (no markdown, no code fences):
{
  ""response_document"": ""## Executive Summary\n\n...\\n\\n## Security & Compliance\\n\n...\\n\\n## Technical Capabilities\\n\n..."",
  ""executive_summary"": ""Brief 2-3 paragraph executive summary"",
  ""total_requirements"": 15,
  ""fully_addressed"": 12,
  ""partially_addressed"": 2,
  ""not_addressed"": 1
}

Quality requirements:
- Professional tone suitable for client submission.
- Organize by category with clear headings.
- Include specific certifications, frameworks, and evidence.
- Do NOT mention internal gaps or non-compliance — only present strengths.
- Use tables for compliance matrix where appropriate.";

        // ── Contract query synthesis ──────────────────────────────────────────

        public const string ContractSynthesisSystemPrompt =
            "You are a contract analysis assistant. Answer the user's question using ONLY the " +
            "numbered context blocks provided below. For every claim you make, cite the source " +
            "using [source_N] notation corresponding to the context block number. " +
            "If the context does not contain sufficient information to answer the question, " +
            "state that explicitly. Do not speculate or use outside knowledge. " +
            "Format the answer using Markdown: use ## headings to organise sections, " +
            "**bold** for key terms, bullet lists for enumerated points, and keep paragraphs short. " +
            "Always respond with valid JSON only.";

        public const string ContractSynthesisPrompt =
            @"Contract context blocks retrieved from the indexed corpus:

{{retrieved_context}}

Question: {{question}}

Provide a comprehensive answer formatted in Markdown (headings, bold, bullets).
After the answer, output a JSON object with:
- ""answer"": your Markdown-formatted answer with inline [source_N] citations
- ""citations"": array of objects, each with: source_id (e.g. ""source_1""), document_id, title, excerpt

Respond ONLY with valid JSON (no outer markdown fences):
{""answer"": ""## Overview\n\nYour **synthesized** answer with [source_1] citations...\n\n## Key Findings\n\n- Point 1 [source_2]\n- Point 2 [source_3]"", ""citations"": [{""source_id"": ""source_1"", ""document_id"": ""doc_id"", ""title"": ""Contract Title"", ""excerpt"": ""relevant excerpt from context""}]}";

        // ── Gap report (#31) ─────────────────────────────────────────────────

        public const string GapReportSystemPrompt =
            "You are a compliance gap analyst. Given all compliance findings across all requirements, " +
            "generate a comprehensive compliance gap report. Prioritize by risk level. " +
            "Include an executive summary, detailed findings, and remediation roadmap. " +
            "Return only valid JSON with no markdown code fences.";

        public const string GapReportPrompt =
            @"Compliance findings across all requirements:
{{compliance_findings}}

Policy alignment results:
{{policy_alignment_summary}}

Generate a compliance gap report. Return ONLY valid JSON (no markdown, no code fences):
{
  ""executive_summary"": ""Overall compliance posture summary..."",
  ""overall_risk"": ""medium"",
  ""total_requirements"": 15,
  ""compliant"": 8,
  ""partially_compliant"": 5,
  ""non_compliant"": 2,
  ""critical_gaps"": [
    {
      ""requirement_id"": ""REQ-005"",
      ""framework"": ""GDPR"",
      ""gap"": ""Missing Data Protection Impact Assessment"",
      ""risk_level"": ""critical"",
      ""remediation"": ""Conduct DPIA before contract execution"",
      ""estimated_effort"": ""2-3 weeks""
    }
  ],
  ""remediation_roadmap"": ""Prioritized list of actions...""
}";
    }

    public static class Messages
    {
        public const string EmptyRfpText           = "RFP document text cannot be empty.";
        public const string NoCapabilities         = "Company capabilities text cannot be empty.";
        public const string NoPolicies             = "Internal policies text cannot be empty.";
        public const string OpenAiKeyNotConfigured = "OpenAI API key is not configured. Add it to appsettings.local.json.";
        public const string WorkflowFailed         = "RFP compliance analysis failed. Please try again.";
        public const string IngestFailed           = "Document ingest failed. Please try again.";
        public const string UnexpectedError        = "An unexpected error occurred.";
        public static string DocumentTooLong(int maxChars) => $"Document text must not exceed {maxChars:N0} characters.";
    }
}
