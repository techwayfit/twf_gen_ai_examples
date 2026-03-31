using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using TwfAiFramework.Core;
using TwfAiFramework.Nodes.AI;
using TwfAiFramework.Nodes.Data;
using _002_EmailDraftingAssistant.Models;
using _002_EmailDraftingAssistant.Services;

namespace _002_EmailDraftingAssistant.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AIServiceController : ControllerBase
{
    private readonly ILogger<AIServiceController> _logger;
    private readonly IConfiguration _configuration;
    private readonly EmailService _emailService;

    public AIServiceController(
        ILogger<AIServiceController> logger,
        IConfiguration configuration,
        EmailService emailService)
    {
        _logger = logger;
        _configuration = configuration;
        _emailService = emailService;
    }

    /// <summary>
    /// Streams a reply draft using a multi-node TwfAiFramework workflow:
    ///   1. Validate input
    ///   2. Load email data, thread history, and sender history
    ///   3. Analyze customer sentiment (LlmNode)
    ///   4. Branch: summarize thread if one exists (LlmNode), otherwise set defaults
    ///   5. Assemble enriched prompt and stream the final reply
    /// </summary>
    [HttpPost("draft-reply")]
    public async Task DraftReplyStream([FromBody] DraftReplyRequest request, CancellationToken ct)
    {
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        var apiKey = _configuration["OpenAI:ApiKey"];
        if (string.IsNullOrEmpty(apiKey) || apiKey == "your-openai-api-key-here")
        {
            await WriteSseError("OpenAI API key is not configured.", ct);
            return;
        }

        // OnChunk is called synchronously per token by the framework's LlmNode.
        // AllowSynchronousIO = true is set in Program.cs so Response.Body.Write works here.
        Action<string> onChunk = chunk =>
        {
            var bytes = Encoding.UTF8.GetBytes(
                $"data: {JsonSerializer.Serialize(new { delta = chunk })}\n\n");
            Response.Body.Write(bytes);
            Response.Body.Flush();
        };

        var llmConfig = BuildLlmConfig(apiKey);
        var workflow  = BuildReplyWorkflow(llmConfig, onChunk);
        var wfContext = new WorkflowContext("ReplyDrafter", _logger);

        var input = WorkflowData.From("message_id", request.MessageId)
                                .Set("requested_tone", request.Tone ?? "professional");

        var result = await workflow.RunAsync(input, wfContext, ct);

        if (!result.IsSuccess)
        {
            await WriteSseError(result.ErrorMessage ?? "Workflow failed — unable to process the email.", ct);
            return;
        }

        if (result.Data.Has("gather_error"))
        {
            await WriteSseError(result.Data.GetString("gather_error") ?? "Email not found.", ct);
            return;
        }

        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes("data: [DONE]\n\n"), ct);
        await Response.Body.FlushAsync(ct);
    }

    // ── Workflow builder ──────────────────────────────────────────────────────

    private LlmConfig BuildLlmConfig(string apiKey)
    {
        var model    = _configuration["OpenAI:Model"]    ?? LlmConfig.OpenAI(apiKey).Model;
        var endpoint = _configuration["OpenAI:Endpoint"] ?? LlmConfig.OpenAI(apiKey).ApiEndpoint;
        return LlmConfig.LmServer(
            model: model, 
            apiKey: apiKey, 
            apiEndpoint: endpoint);
    }

    private Workflow BuildReplyWorkflow(LlmConfig llmConfig, Action<string> onChunk)
    {
        var workflow = Workflow.Create("ReplyDrafter").UseLogger(_logger);

        // ── Node 1: Validate ──────────────────────────────────────────────
        workflow.AddNode(new FilterNode("ValidateInput").RequireNonEmpty("message_id"));

        // ── Step 2: Load email, thread, and sender history ────────────────
        workflow.AddStep("GatherEmailData", (data, _) =>
        {
            var msgId = data.GetRequiredString("message_id");
            var email = _emailService.GetById(msgId);

            if (email is null)
                return Task.FromResult(data.Set("gather_error", $"Email '{msgId}' not found."));

            var thread = _emailService.GetThread(email.ThreadId);
            var threadMessages = thread
                .Where(m => m.MessageId != email.MessageId)
                .OrderBy(m => m.Date)
                .ToList();

            var senderHistory = _emailService.GetBySender(email.SenderEmail, 10)
                .Where(e => e.MessageId != email.MessageId)
                .OrderByDescending(e => e.Date)
                .Take(4)
                .ToList();

            // Format thread for PromptBuilderNode template ({{thread_messages}})
            var threadSb = new StringBuilder();
            foreach (var msg in threadMessages)
            {
                threadSb.AppendLine($"From: {msg.SenderName} | {msg.Date:MMM d, yyyy HH:mm}");
                threadSb.AppendLine(msg.Body.Length > 400 ? msg.Body[..400] + "..." : msg.Body);
                threadSb.AppendLine();
            }

            // Format sender history for final prompt assembly
            var historySb = new StringBuilder();
            if (senderHistory.Count > 0)
            {
                historySb.AppendLine("=== PREVIOUS EMAILS FROM THIS CUSTOMER ===");
                foreach (var msg in senderHistory)
                {
                    historySb.AppendLine($"Subject: {msg.Subject} | {msg.Date:MMM d, yyyy}");
                    historySb.AppendLine(msg.Body.Length > 300 ? msg.Body[..300] + "..." : msg.Body);
                    historySb.AppendLine();
                }
                historySb.AppendLine();
            }

            return Task.FromResult(data
                .Set("email_body",      email.Body)
                .Set("sender_name",     email.SenderName)
                .Set("sender_email",    email.SenderEmail)
                .Set("subject",         email.Subject)
                .Set("date",            email.Date.ToString("ddd, MMM d, yyyy HH:mm"))
                .Set("thread_messages", threadSb.ToString())
                .Set("sender_history",  historySb.ToString())
                .Set("has_thread",      threadMessages.Count > 0));
        });

        // ── Nodes 3–5: Sentiment analysis ─────────────────────────────────
        // PromptBuilderNode interpolates {{email_body}} from WorkflowData
        workflow.AddNode(new PromptBuilderNode(
            name:           "SentimentAnalysisPrompt",
            promptTemplate: Constants.Prompts.SentimentAnalysisPrompt,
            systemTemplate: Constants.Prompts.SentimentAnalysisSystemPrompt));

        workflow.AddNode(
            new LlmNode("SentimentAnalyzer", llmConfig with { MaxTokens = 200 }),
            NodeOptions.WithRetry(2));

        // Parse JSON keys: sentiment, urgency, key_issues → WorkflowData keys
        workflow.AddNode(OutputParserNode.WithMapping("SentimentParser",
            ("sentiment",  "sentiment"),
            ("urgency",    "urgency"),
            ("key_issues", "key_issues")));

        // ── Branch 4: Thread analysis (only when thread messages exist) ───
        Action<Workflow> withThreadBranch = flow =>
        {
            // PromptBuilderNode interpolates {{thread_messages}} from WorkflowData
            workflow.AddNode(new PromptBuilderNode(
                name:           "ThreadAnalysisPrompt",
                promptTemplate: Constants.Prompts.ThreadAnalysisPrompt,
                systemTemplate: Constants.Prompts.ThreadAnalysisSystemPrompt));

            workflow.AddNode(
                new LlmNode("ThreadAnalyzer", llmConfig with { MaxTokens = 300 }),
                NodeOptions.WithRetry(2));

            // Parse JSON keys: thread_summary, ongoing_issues → WorkflowData keys
            workflow.AddNode(OutputParserNode.WithMapping("ThreadParser",
                ("thread_summary",  "thread_summary"),
                ("ongoing_issues",  "ongoing_issues")));
        };

        Action<Workflow> noThreadBranch = flow =>
        {
            flow.AddStep("NoThreadContext", (data, _) =>
                Task.FromResult(data
                    .Set("thread_summary",  "No previous thread messages.")
                    .Set("ongoing_issues",  "None")));
        };

        workflow.Branch(data => data.Get<bool>("has_thread"), withThreadBranch, noThreadBranch);

        // ── Step 5: Assemble final enriched prompt ────────────────────────
        workflow.AddStep("BuildFinalPrompt", (data, _) =>
        {
            var finalPrompt = Constants.Prompts.ReplyDraftUserPrompt
                .Replace("{senderName}",    data.GetString("sender_name")    ?? string.Empty)
                .Replace("{senderEmail}",   data.GetString("sender_email")   ?? string.Empty)
                .Replace("{subject}",       data.GetString("subject")        ?? string.Empty)
                .Replace("{date}",          data.GetString("date")           ?? string.Empty)
                .Replace("{body}",          data.GetString("email_body")     ?? string.Empty)
                .Replace("{sentiment}",     data.GetString("sentiment")      ?? "neutral")
                .Replace("{urgency}",       data.GetString("urgency")        ?? "medium")
                .Replace("{keyIssues}",     data.GetString("key_issues")     ?? string.Empty)
                .Replace("{threadSummary}", data.GetString("thread_summary") ?? string.Empty)
                .Replace("{ongoingIssues}", data.GetString("ongoing_issues") ?? "None")
                .Replace("{senderHistory}", data.GetString("sender_history") ?? string.Empty)
                .Replace("{tone}",          data.GetString("requested_tone") ?? "professional");

            return Task.FromResult(data.Set("final_user_prompt", finalPrompt));
        });

        // ── Node 6: Feed assembled prompt into PromptBuilderNode ──────────
        // {{final_user_prompt}} is interpolated from WorkflowData, passing the
        // fully-built prompt through to the LlmNode as its user message.
        // WithSystem(name, promptTemplate, systemTemplate) — positional args.
        workflow.AddNode(PromptBuilderNode.WithSystem(
            "FinalReplyPrompt",
            "{{final_user_prompt}}",
            Constants.Prompts.ReplyDraftSystemPrompt));

        // ── Node 7: Streaming LlmNode — tokens go directly to SSE response ─
        workflow.AddNode(
            new LlmNode("FinalReplyStreamer", llmConfig with
            {
                Stream    = true,
                OnChunk   = onChunk,
                MaxTokens = 1024
            }),
            NodeOptions.WithRetry(1));

        return workflow;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task WriteSseError(string message, CancellationToken ct)
    {
        var payload = $"data: {JsonSerializer.Serialize(new { error = message })}\n\n";
        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(payload), ct);
        await Response.Body.FlushAsync(ct);
    }

    /// <summary>Returns the health status of the AI service.</summary>
    [HttpGet("health")]
    public IActionResult Health() =>
        Ok(new { status = "healthy", service = Constants.AppName, timestamp = DateTime.UtcNow });
}

// ── Request / Response models ─────────────────────────────────────────────────

public class DraftReplyRequest
{
    public string MessageId { get; set; } = string.Empty;
    public string Tone      { get; set; } = "professional";
}


