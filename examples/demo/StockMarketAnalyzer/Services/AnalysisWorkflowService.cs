using System.Text.Json;
using System.Text.Json.Serialization;
using _StockMarketAnalyzer.Models;
using Twf.Flow.Core;
using Twf.Flow.Core.Extensions;
using Twf.Flow.Nodes.Control;
using Twf.Flow.Nodes.Data;

namespace _StockMarketAnalyzer.Services;

/// <summary>
/// Builds and executes the stock analysis pipeline using Twf.Flow.
///
/// Pipeline stages:
///   1. ValidateInput      — FilterNode:          ensure symbol is present
///   2. FetchData          — AddStep:             call YahooFinanceService for quote + history
///   3. AnalyzeTrend       — AddStep:             LLM trend analysis
///   4. GenerateRecommendation — AddStep:         LLM buy/sell/hold recommendation
///   5. AssessRisk         — AddStep:             LLM risk identification
///   6. EmitResult         — AddStep:             assemble and fire SSE complete event
/// </summary>
public class AnalysisWorkflowService(
    ILogger<AnalysisWorkflowService> logger,
    LlmService llmService,
    YahooFinanceService yahooService)
{
    private readonly ILogger<AnalysisWorkflowService> _logger = logger;
    private readonly LlmService _llmService = llmService;
    private readonly YahooFinanceService _yahooService = yahooService;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── Public entry point ────────────────────────────────────────────────────

    public async Task<WorkflowResult> RunAsync(
        string symbol,
        Func<StageEvent, Task> sendStageAsync,
        Func<StockAnalysisResult, Task> sendCompleteAsync,
        string apiKey,
        string llmModel,
        string llmEndpoint,
        CancellationToken ct = default)
    {
        var workflow = BuildWorkflow(sendStageAsync, sendCompleteAsync, apiKey, llmModel, llmEndpoint, ct);

        var input = WorkflowData
            .From("symbol", symbol.ToUpper().Trim());

        var context = new WorkflowContext("StockAnalyzer", _logger);
        return await workflow.RunAsync(input, context, ct);
    }

    // ── Workflow builder ──────────────────────────────────────────────────────

    private Workflow BuildWorkflow(
        Func<StageEvent, Task> sendStageAsync,
        Func<StockAnalysisResult, Task> sendCompleteAsync,
        string apiKey,
        string llmModel,
        string llmEndpoint,
        CancellationToken ct = default)
    {
        var workflow = Workflow.Create("StockAnalyzer").UseLogger(_logger);

        // ── 1. Validate input ────────────────────────────────────────────────
        workflow.AddNode(
            new FilterNode("ValidateInput")
                .RequireNonEmpty("symbol")
                .MaxLength("symbol", 10));

        // ── 2. Fetch stock data ──────────────────────────────────────────────
        workflow.AddStep("FetchData", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Fetching stock data...", 1, 3));

            var symbol = data.GetString("symbol") ?? string.Empty;

            var quote = await _yahooService.GetQuoteAsync(symbol, ct);
            if (quote == null)
                throw new InvalidOperationException($"Stock symbol '{symbol}' not found.");

            var history = await _yahooService.GetHistoryAsync(symbol, months: 3, ct);

            _logger.LogInformation("History fetched: {Count} data points", history.Count);

            data.Set("current_price", quote.Price.ToString());
            data.Set("volume", quote.Volume.ToString());
            data.Set("quote_json", JsonSerializer.Serialize(quote, JsonOpts));
            data.Set("history_json", JsonSerializer.Serialize(history, JsonOpts));

            // Format price history for prompts (last 30 days)
            var recentHistory = history.TakeLast(30).ToList();
            var historyText = string.Join("\n", recentHistory.Select(p =>
                $"{p.Date:yyyy-MM-dd}: Open={p.Open:F2}, High={p.High:F2}, Low={p.Low:F2}, Close={p.Close:F2}, Vol={p.Volume:N0}"));
            data.Set("price_history", historyText);

            _logger.LogInformation("Fetched data for {Symbol}: {Price} ({Change}%)",
                symbol, quote.Price, quote.ChangePercent);

            return data;
        });

        // ── 3. Trend analysis ───────────────────────────────────────────────
        workflow.AddStep("AnalyzeTrend", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Analyzing trend...", 2, 3));

            var symbol      = data.GetString("symbol")       ?? string.Empty;
            var currentPrice = data.GetString("current_price") ?? "0";
            var volume      = data.GetString("volume")        ?? "0";
            var priceHistory = data.GetString("price_history") ?? string.Empty;

            var systemPrompt = Constants.Prompts.TrendSystemPrompt;
            var userPrompt = Constants.Prompts.TrendPrompt
                .Replace("{{symbol}}", symbol)
                .Replace("{{current_price}}", currentPrice)
                .Replace("{{volume}}", volume)
                .Replace("{{price_history}}", priceHistory);

            var json = await _llmService.CompleteAsync(systemPrompt, userPrompt, apiKey, llmModel, llmEndpoint, 800, ct);
            var trend = ParseJson<TrendAnalysis>(StripCodeFences(json));
            data.Set("trend_analysis", json);

            _logger.LogInformation("Trend analysis complete for {Symbol}: {Direction}", symbol, trend?.Direction);
            return data;
        });

        // ── 4. Recommendation ───────────────────────────────────────────────
        workflow.AddStep("GenerateRecommendation", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Generating recommendation...", 2, 3));

            var symbol       = data.GetString("symbol")        ?? string.Empty;
            var currentPrice = data.GetString("current_price") ?? "0";
            var trendAnalysis = data.GetString("trend_analysis") ?? "{}";

            var systemPrompt = Constants.Prompts.RecommendationSystemPrompt;
            var userPrompt = Constants.Prompts.RecommendationPrompt
                .Replace("{{symbol}}", symbol)
                .Replace("{{current_price}}", currentPrice)
                .Replace("{{trend_analysis}}", StripCodeFences(trendAnalysis));

            var json = await _llmService.CompleteAsync(systemPrompt, userPrompt, apiKey, llmModel, llmEndpoint, 800, ct);
            data.Set("recommendation", json);

            _logger.LogInformation("Recommendation generated for {Symbol}", symbol);
            return data;
        });

        // ── 5. Risk assessment ──────────────────────────────────────────────
        workflow.AddStep("AssessRisk", async (data, _) =>
        {
            await sendStageAsync(new StageEvent("Identifying risks...", 3, 3));

            var symbol       = data.GetString("symbol")         ?? string.Empty;
            var currentPrice = data.GetString("current_price")  ?? "0";
            var priceHistory = data.GetString("price_history")  ?? string.Empty;
            var trendAnalysis = data.GetString("trend_analysis") ?? "{}";
            var recommendation = data.GetString("recommendation") ?? "{}";

            var systemPrompt = Constants.Prompts.RiskSystemPrompt;
            var userPrompt = Constants.Prompts.RiskPrompt
                .Replace("{{symbol}}", symbol)
                .Replace("{{current_price}}", currentPrice)
                .Replace("{{price_history}}", priceHistory)
                .Replace("{{trend_analysis}}", StripCodeFences(trendAnalysis))
                .Replace("{{recommendation}}", StripCodeFences(recommendation));

            var json = await _llmService.CompleteAsync(systemPrompt, userPrompt, apiKey, llmModel, llmEndpoint, 800, ct);
            data.Set("risk_assessment", json);

            _logger.LogInformation("Risk assessment complete for {Symbol}", symbol);
            return data;
        });

        // ── 6. Assemble and emit result ─────────────────────────────────────
        workflow.AddStep("EmitResult", async (data, _) =>
        {
            var symbol        = data.GetString("symbol")          ?? string.Empty;
            var quoteJson     = data.GetString("quote_json")      ?? "{}";
            var historyJson   = data.GetString("history_json")    ?? "[]";
            var trendJson     = data.GetString("trend_analysis")  ?? "{}";
            var recJson       = data.GetString("recommendation")  ?? "{}";
            var riskJson      = data.GetString("risk_assessment") ?? "{}";

            _logger.LogInformation("[Workflow] Raw recommendation JSON: {RecJson}", recJson);

            var quote   = JsonSerializer.Deserialize<StockQuote>(quoteJson, JsonOpts) ?? new StockQuote(symbol, symbol, 0, 0, 0, 0);
            var history = JsonSerializer.Deserialize<List<HistoricalPrice>>(historyJson, JsonOpts) ?? new();

            _logger.LogInformation("[Workflow] Deserialized history: {Count} data points", history.Count);

            var trend   = ParseJson<TrendAnalysis>(StripCodeFences(trendJson))  ?? new TrendAnalysis("Sideways", "Analysis unavailable", new());
            var rec     = ParseJson<StockRecommendation>(StripCodeFences(recJson)) ?? new StockRecommendation("Hold", 0, "Analysis unavailable", 0);
            var risk    = ParseJson<RiskAssessment>(StripCodeFences(riskJson))     ?? new RiskAssessment("Medium", "Risk assessment unavailable", new());

            _logger.LogInformation("[Workflow] Parsed recommendation - Action: {Action}, TargetPrice: {TargetPrice}, Confidence: {Confidence}", 
                rec.Action, rec.TargetPrice, rec.Confidence);

            var result = new StockAnalysisResult(
                Symbol: symbol,
                Trend: trend,
                Recommendation: rec,
                Risk: risk,
                Quote: quote,
                PriceHistory: history,
                AnalyzedAt: DateTime.UtcNow);

            await sendCompleteAsync(result);
            return data;
        });

        return workflow;
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    private T? ParseJson<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON for {Type}", typeof(T).Name);
            return default;
        }
    }

    private static string StripCodeFences(string raw)
    {
        var s = raw.Trim();
        if (s.StartsWith("```"))
        {
            var firstNewline = s.IndexOf('\n');
            if (firstNewline > 0) s = s[(firstNewline + 1)..];
            if (s.EndsWith("```")) s = s[..^3];
        }
        return s.Trim();
    }
}
