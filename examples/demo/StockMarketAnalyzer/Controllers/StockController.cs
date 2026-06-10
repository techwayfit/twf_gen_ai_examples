using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using _StockMarketAnalyzer.Models;
using _StockMarketAnalyzer.Services;

namespace _StockMarketAnalyzer.Controllers;

[ApiController]
[Route("api/[controller]")]
[IgnoreAntiforgeryToken]
public class StockController : ControllerBase
{
    private readonly ILogger<StockController> _logger;
    private readonly IConfiguration _configuration;
    private readonly AnalysisWorkflowService _workflowService;
    private readonly YahooFinanceService _yahooService;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public StockController(
        ILogger<StockController> logger,
        IConfiguration configuration,
        AnalysisWorkflowService workflowService,
        YahooFinanceService yahooService)
    {
        _logger = logger;
        _configuration = configuration;
        _workflowService = workflowService;
        _yahooService = yahooService;
    }

    /// <summary>
    /// Analyzes a stock and streams progress as SSE events.
    ///
    /// Event types:
    ///   stage    — { message, stageIndex, totalStages }
    ///   complete — StockAnalysisResult
    ///   error    — { error }
    /// </summary>
    [HttpPost("analyze")]
    public async Task Analyze([FromBody] StockAnalysisRequest request, CancellationToken ct)
    {
        Response.Headers["Content-Type"]      = "text/event-stream";
        Response.Headers["Cache-Control"]     = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        async Task SendAsync(string evt, object data)
        {
            var json = JsonSerializer.Serialize(data, JsonOpts);
            await Response.WriteAsync($"event: {evt}\ndata: {json}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        var openAiKey = _configuration["OpenAI:ApiKey"];
        if (string.IsNullOrEmpty(openAiKey) || openAiKey == "your-openai-api-key-here")
        {
            await SendAsync("error", new { error = Constants.Messages.OpenAiKeyNotConfigured });
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Symbol))
        {
            await SendAsync("error", new { error = Constants.Messages.EmptySymbol });
            return;
        }

        var llmModel    = _configuration["OpenAI:ChatModel"] ?? "gpt-4o-mini";
        var llmEndpoint = _configuration["OpenAI:Endpoint"]  ?? "https://api.openai.com/v1/chat/completions";

        try
        {
            var result = await _workflowService.RunAsync(
                symbol:          request.Symbol.Trim(),
                sendStageAsync:  stage  => SendAsync("stage",    stage),
                sendCompleteAsync: result => SendAsync("complete", result),
                apiKey:          openAiKey,
                llmModel:        llmModel,
                llmEndpoint:     llmEndpoint,
                ct:              ct);

            if (!result.IsSuccess)
            {
                _logger.LogError("Stock analysis workflow failed: {Error}", result.ErrorMessage);
                await SendAsync("error",
                    new { error = result.ErrorMessage ?? Constants.Messages.WorkflowFailed });
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during stock analysis");
            try { await SendAsync("error", new { error = Constants.Messages.UnexpectedError }); }
            catch { /* response may be gone */ }
        }
    }

    /// <summary>Gets a quick quote for a stock symbol (no AI).</summary>
    [HttpGet("quote/{symbol}")]
    public async Task<IActionResult> GetQuote(string symbol, CancellationToken ct)
    {
        var quote = await _yahooService.GetQuoteAsync(symbol, ct);
        if (quote == null)
            return NotFound(new { error = Constants.Messages.SymbolNotFound });

        return Ok(quote);
    }

    /// <summary>Gets historical price data for charting.</summary>
    [HttpGet("history/{symbol}")]
    public async Task<IActionResult> GetHistory(string symbol, [FromQuery] int months = 3, CancellationToken ct = default)
    {
        var history = await _yahooService.GetHistoryAsync(symbol, months, ct);
        return Ok(history);
    }

    /// <summary>Searches for stock symbols matching a query.</summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 1)
            return Ok(new List<StockSearchResult>());

        try
        {
            var results = await _yahooService.SearchAsync(q.Trim(), ct);
            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Search failed for query: {Query}", q);
            return Ok(new List<StockSearchResult>());
        }
    }
}

// ── Request model ─────────────────────────────────────────────────────────────

public class StockAnalysisRequest
{
    public string Symbol { get; set; } = string.Empty;
}
