using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TwfAiFramework.Core;
using TwfAiFramework.Nodes.AI;
using TwfAiFramework.Nodes.Control;
using TwfAiFramework.Nodes.Data;

namespace _001_CustomerSupportChatbot.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatApiController : ControllerBase
{
    private readonly ILogger<ChatApiController> _logger;
    private readonly IConfiguration _configuration;
    private static readonly Dictionary<string, WorkflowContext> _sessions = new();

    public ChatApiController(ILogger<ChatApiController> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    [HttpPost("message")]
    public async Task<IActionResult> SendMessage([FromBody] ChatRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest(new { error = Constants.Messages.EmptyMessage });
            }

            // Get or create session context
            var sessionId = request.SessionId ?? Guid.NewGuid().ToString();
            if (!_sessions.ContainsKey(sessionId))
            {
                var context = new WorkflowContext("CustomerSupportBot", _logger);
                context.SetState("company_name", Constants.CompanyName);
                context.SetState("support_tier", Constants.DefaultResponseType);
                context.SetState("bot_type", request.BotType ?? "support");
                _sessions[sessionId] = context;
            }
            var botType = request.BotType;
            var sessionContext = _sessions[sessionId];

            // Get API key from configuration
            var apiKey = _configuration["OpenAI:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                return StatusCode(500, new { error = Constants.Messages.OpenApiKeyNotConfigured });
            }

            // Build the customer support workflow
            var workflow = botType == "basic" ?
                BuildSimpleWorkFlow(apiKey) :
                botType == "safetycheck" ? BuildWorkFlowWithSafetyCheck(apiKey) :
                 BuildWorkFlowWithSentimentAnalyzer(apiKey);

            // Prepare input data
            var input = WorkflowData.From("user_message", request.Message)
                         .Set("company_name", Constants.CompanyName);

            // Run the workflow
            var result = await workflow.RunAsync(input, sessionContext);

            if (result.IsSuccess)
            {
                var response = result.Data.GetString("llm_response") ?? Constants.Messages.RequestCouldNotProcessed;
                var responseType = result.Data.GetString("response_type") ?? Constants.DefaultResponseType;
                var sentiment = result.Data.GetString("sentiment");

                return Ok(new ChatResponse
                {
                    SessionId = sessionId,
                    Message = response,
                    ResponseType = responseType,
                    Sentiment = sentiment,
                    Timestamp = DateTime.UtcNow
                });
            }
            else
            {
                _logger.LogError("Workflow failed: {Error}", result.ErrorMessage);
                //return StatusCode(500, new { error = Constants.Messages.FailedToProcessRequest });
                return StatusCode(500, new { error = result.ErrorMessage });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chat message");
            return StatusCode(500, new { error = "An unexpected error occurred" });
        }
    }

    [HttpDelete("session/{sessionId}")]
    public IActionResult ClearSession(string sessionId)
    {
        if (_sessions.ContainsKey(sessionId))
        {
            _sessions.Remove(sessionId);
            return Ok(new { message = Constants.Messages.SessionCleared });
        }
        return NotFound(new { error = Constants.Messages.SessionNotFound });
    }
    private Workflow BuildWorkflow(string apiKey, out LlmConfig llmConfig)
    {
        //llmConfig = LlmConfig.OpenAI(apiKey);
        var model=_configuration["OpenAI:Model"] ?? "gpt-3.5-turbo";
         var endpoint=_configuration["OpenAI:Endpoint"] ?? "https://api.openai.com/v1/chat/completions";
        llmConfig= LlmConfig.LmServer(
            model: model,
            apiKey: apiKey,
            apiEndpoint: endpoint
            );

        var workflow = Workflow.Create("BasicAIResponse").UseLogger(_logger);
        var inputFilter = new FilterNode("ValidateInput").RequireNonEmpty("user_message").MaxLength("user_message", 500);
        workflow.AddNode(inputFilter);
        return workflow;
    }

    private Workflow BuildSimpleWorkFlow(string apiKey)
    {
        LlmConfig llmConfig;
        Workflow workflow = BuildWorkflow(apiKey, out llmConfig);
        var promptNode = new PromptBuilderNode(
            name: "BasicPrompt",
            promptTemplate: Constants.Prompts.BasicSupportPrompt,
            systemTemplate: Constants.Prompts.BasicSupportSystemTemplate
        );
        workflow.AddNode(promptNode);
        var llmNode = new LlmNode("BasicResponseLLMNode", llmConfig with { MaxTokens = 300 });
        workflow.AddNode(llmNode, NodeOptions.WithRetry(2));


        return workflow;
    }

    private Workflow BuildWorkFlowWithSafetyCheck(string apiKey)
    {
        LlmConfig llmConfig;
        Workflow workflow = BuildWorkflow(apiKey, out llmConfig);
        var safetyCheckPromptNode = new PromptBuilderNode(
            name: "SafetyCheckPrompt",
            promptTemplate: Constants.Prompts.SafetyCheckPrompt,
            systemTemplate: Constants.Prompts.SafetyCheckSystemPrompt);
        var safetyCheckLlmNode = new LlmNode("SafetyChecker", llmConfig with { MaxTokens = 100 });
        workflow.AddNode(safetyCheckPromptNode)
            .AddNode(safetyCheckLlmNode, NodeOptions.WithRetry(2))
            .AddNode(OutputParserNode.WithMapping("SafetyResponseParser", ("is_safe", "isSafe"), ("reason", "safetyReason")));

        Action<Workflow> safeFlow = flow =>
        {
            var promptNode = new PromptBuilderNode(
                name: "BasicPrompt",
                promptTemplate: Constants.Prompts.BasicSupportPrompt,
                systemTemplate: Constants.Prompts.BasicSupportSystemTemplate
                );
            workflow.AddNode(promptNode);
            var llmNode = new LlmNode("BasicResponseLLMNode", llmConfig with { MaxTokens = 300 });
            workflow.AddNode(llmNode, NodeOptions.WithRetry(2));
        };
        Action<Workflow> unSafeFlow = flow =>
        {
            flow.AddStep("RejectUnsafe", (data, _) =>
            Task.FromResult(data.Set("llm_response", Constants.Prompts.UnSafeResponse).Set("response_type", "rejected")));
        };

        workflow.Branch(data => data.Get<bool>("isSafe"), safeFlow, unSafeFlow);


        return workflow;
    }

    private Workflow BuildWorkFlowWithSentimentAnalyzer(string apiKey)
    {
        LlmConfig llmConfig;
        Workflow workflow = BuildWorkflow(apiKey, out llmConfig);
        var safetyCheckPromptNode = new PromptBuilderNode(
            name: "SafetyCheckPrompt",
            promptTemplate: Constants.Prompts.SafetyCheckPrompt,
            systemTemplate: Constants.Prompts.SafetyCheckSystemPrompt);
        var safetyCheckLlmNode = new LlmNode("SafetyChecker", llmConfig with { MaxTokens = 100 });
        workflow.AddNode(safetyCheckPromptNode)
            .AddNode(safetyCheckLlmNode, NodeOptions.WithRetry(2))
            .AddNode(OutputParserNode.WithMapping("SafetyResponseParser", ("is_safe", "isSafe"), ("reason", "safetyReason")));

        Action<Workflow> safeFlow = flow =>
        {
            var sentimentPromptNode = new PromptBuilderNode(
            name: "SentimentAnalyzer",
            promptTemplate: Constants.Prompts.SentimentPrompt);
            var sentimentCheckLlmNode = new LlmNode("SentimentAnalyzer", llmConfig with { MaxTokens = 100 });
            workflow.AddNode(sentimentPromptNode)
                .AddNode(sentimentCheckLlmNode, NodeOptions.WithRetry(2))
                .AddNode(OutputParserNode.WithMapping("SentimentParser", ("sentiment", "sentiment"), ("score", "anger_score")));
            Action<Workflow> angrySentimentFlow = wflow =>
            {
                wflow.AddNode(new PromptBuilderNode(
                    name: "EscalationPrompt",
                    promptTemplate: Constants.Prompts.SentimentEscalationPrompt,
                    systemTemplate: Constants.Prompts.SentimentEscalationSystemPrompt
                    ))
                .AddNode(new LlmNode("EscalationResponder", llmConfig with
                {
                    MaintainHistory = true,
                    MaxTokens = 500
                }))
                .AddStep("TagEscalation", (data, _) =>
                Task.FromResult(data.Set("response_type", "escalation")));
            };
            Action<Workflow> normalSentimentFlow = wflow =>
            {
                wflow.AddNode(new PromptBuilderNode(
                    name: "StandardResponsePrompt",
                    promptTemplate: Constants.Prompts.SentimentNormalPrompt,
                    systemTemplate: Constants.Prompts.SentimentNormalSystemPrompt
                    ))
                .AddNode(new LlmNode("StandardResponder", llmConfig with
                {
                    MaintainHistory = true,
                    MaxTokens = 500
                }))
                .AddStep("TagStandard", (data, _) =>
                Task.FromResult(data.Set("response_type", "standard")));
            };

            flow.Branch(data => data.GetString("sentiment") == "angry" || data.Get<int>("anger_score") >= 7,
                angrySentimentFlow, normalSentimentFlow);

        };
        Action<Workflow> unSafeFlow = flow =>
        {
            flow.AddStep("RejectUnsafe", (data, _) =>
            Task.FromResult(data.Set("llm_response", Constants.Prompts.UnSafeResponse).Set("response_type", "rejected")));
        };

        workflow.Branch(data => data.Get<bool>("isSafe"), safeFlow, unSafeFlow);

        return workflow;
    }
}

public class ChatRequest
{
    public string? SessionId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? BotType { get; set; }
}

public class ChatResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string ResponseType { get; set; } = string.Empty;
    public string? Sentiment { get; set; }
    public DateTime Timestamp { get; set; }
}
