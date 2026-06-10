namespace _038_API_Documentation_Generator;

public static class Constants
{
    public static class Prompts
    {
        public const string DocGenerationSystemPrompt =
            "You are an expert API documentation engineer. Given a source code function or endpoint, " +
            "generate a complete OpenAPI 3.1 spec entry and a Markdown documentation block. " +
            "Be precise about parameter types, request/response schemas, and authentication requirements. " +
            "Generate realistic usage examples in at least one language (curl, Python, JavaScript, or C#).";

        public const string DocGenerationUserPrompt =
            "Generate OpenAPI spec entry and Markdown documentation for this endpoint:\n\n" +
            "Source file: {{file_path}}\n" +
            "Language: {{language}}\n" +
            "Visibility: {{visibility}}\n" +
            "Declaration: {{declaration}}\n" +
            "XML doc comment: {{xml_doc}}\n" +
            "Controller/Class: {{class_name}}\n" +
            "Route prefix: {{route_prefix}}\n\n" +
            "Return ONLY valid JSON with this structure:\n" +
            "{\n" +
            "  \"path\": \"/api/resource\",\n" +
            "  \"http_method\": \"GET\",\n" +
            "  \"operation_id\": \"getResource\",\n" +
            "  \"summary\": \"Short description\",\n" +
            "  \"description\": \"Detailed description\",\n" +
            "  \"tags\": [\"tag1\"],\n" +
            "  \"parameters\": [\n" +
            "    {\n" +
            "      \"name\": \"paramName\",\n" +
            "      \"in\": \"query\",\n" +
            "      \"required\": false,\n" +
            "      \"schema\": { \"type\": \"string\" },\n" +
            "      \"description\": \"Parameter description\"\n" +
            "    }\n" +
            "  ],\n" +
            "  \"request_body\": { /* null if GET/DELETE */ },\n" +
            "  \"responses\": {\n" +
            "    \"200\": { \"description\": \"Successful response\", \"content\": { \"application/json\": { \"schema\": { \"type\": \"object\" } } } }\n" +
            "  },\n" +
            "  \"markdown_doc\": \"## GET /api/resource\\n\\nDescription...\\n\\n### Parameters\\n\\n### Example\\n\\n```curl\\n...\\n```\\n\\n### Response\\n\\n```json\\n...\\n```\",\n" +
            "  \"usage_examples\": {\n" +
            "    \"curl\": \"curl -X GET ...\",\n" +
            "    \"python\": \"import requests\\n...\",\n" +
            "    \"csharp\": \"var client = new HttpClient();\\n...\"\n" +
            "  }\n" +
            "}";

        public const string SpecAssemblyPrompt =
            "You are assembling a complete OpenAPI 3.1 specification from individual endpoint entries.\n\n" +
            "Combine the following endpoint specs into a single valid OpenAPI 3.1 document. " +
            "Use the provided API title and version. Merge shared schemas and deduplicate.\n\n" +
            "API Title: {{api_title}}\n" +
            "API Version: {{api_version}}\n" +
            "Base URL: {{base_url}}\n\n" +
            "Endpoint entries:\n{{endpoint_entries}}\n\n" +
            "Return the complete OpenAPI 3.1 spec as a valid JSON object.";
    }

    public static class Messages
    {
        public const string OpenAiKeyNotConfigured = "OpenAI API key is not configured. Add it to appsettings.local.json.";
        public const string RepoPathRequired = "Repository path is required.";
        public const string RepoPathNotFound = "Repository path does not exist.";
        public const string NoApiFunctionsFound = "No public API functions or endpoints found in the specified codebase.";
        public const string DocGenerationFailed = "Documentation generation failed. Check logs for details.";
        public const string ScanInProgress = "Scan and documentation generation is in progress.";
    }

    public static class RegexPatterns
    {
        public const string ControllerPattern = @"\[Route\((?:\""[^\""]+\""|'[^']+')\)\]|\[ApiController\]|: ControllerBase|: Controller";
        public const string HttpMethodPattern = @"\[Http(Get|Post|Put|Delete|Patch|Head|Options)\]";
        public const string RouteAttributePattern = @"\[Route\((?:\""(?<route>[^\""]+)\""|'(?<route>[^']+)')\)\]";
        public const string PublicMethodPattern = @"(public|internal)\s+(async\s+)?(static\s+)?(partial\s+)?(?<return>\w+[?]?|<[^>]+>)\s+(?<name>\w+)\s*\(";
        public const string XmlDocSummary = @"///\s*<summary>\s*(?<summary>[^<]+)\s*</summary>";
        public const string XmlDocParam = @"///\s*<param\s+name=\""(?<name>[^\""]+)\"">(?<desc>[^<]+)</param>";
        public const string XmlDocReturns = @"///\s*<returns>(?<returns>[^<]+)</returns>";
    }
}
