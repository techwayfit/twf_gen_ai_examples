using _StockMarketAnalyzer.Models;

namespace _StockMarketAnalyzer;

public static class Constants
{
    public static class Prompts
    {
        // ── Trend Analysis ───────────────────────────────────────────────────

        public const string TrendSystemPrompt =
            "You are a senior financial analyst specializing in technical analysis. " +
            "Analyze the provided stock data and identify the current trend, key indicators, and momentum. " +
            "Always respond with valid JSON only — no explanation, no markdown.";

        public const string TrendPrompt = @"Analyze the stock {{symbol}} and determine its current trend.

CURRENT PRICE: {{current_price}}
VOLUME: {{volume}}

RECENT PRICE HISTORY (last 30 trading days):
{{price_history}}

TASK:
1. Determine the overall trend direction (Bullish, Bearish, or Sideways)
2. Identify key technical indicators (moving averages, RSI signals, support/resistance levels)
3. Provide a concise summary of the trend

Return ONLY a JSON object with this exact structure:
{""direction"": ""Bullish|Bearish|Sideways"", ""summary"": ""..."", ""indicators"": [""..."", ""...""]}";

        // ── Recommendation ───────────────────────────────────────────────────

        public const string RecommendationSystemPrompt =
            "You are a licensed investment advisor providing stock recommendations. " +
            "Base your recommendation on the provided trend analysis and price data. " +
            "Always respond with valid JSON only — no explanation, no markdown.";

        public const string RecommendationPrompt = @"Provide an investment recommendation for {{symbol}}.

CURRENT PRICE: {{current_price}}

TREND ANALYSIS:
{{trend_analysis}}

TASK:
1. Recommend a clear action: Buy, Sell, or Hold
2. Provide a target price (for Buy: upside target; for Sell: downside target; for Hold: no change)
3. Explain your reasoning concisely
4. Rate your confidence from 0.0 to 1.0

Return ONLY a JSON object with this exact structure:
{""action"": ""Buy|Sell|Hold"", ""target_price"": 0.00, ""reasoning"": ""..."", ""confidence"": 0.0}";

        // ── Risk Assessment ──────────────────────────────────────────────────

        public const string RiskSystemPrompt =
            "You are a risk management expert specializing in equity markets. " +
            "Assess the investment risks for the given stock based on its price history and analysis. " +
            "Always respond with valid JSON only — no explanation, no markdown.";

        public const string RiskPrompt = @"Assess the investment risks for {{symbol}}.

CURRENT PRICE: {{current_price}}

RECENT PRICE HISTORY (last 30 trading days):
{{price_history}}

TREND ANALYSIS:
{{trend_analysis}}

RECOMMENDATION:
{{recommendation}}

TASK:
1. Classify overall risk as Low, Medium, or High
2. Identify specific risk factors (volatility, drawdown, sector risk, market risk, etc.)
3. Provide a risk summary

Return ONLY a JSON object with this exact structure:
{""level"": ""Low|Medium|High"", ""summary"": ""..."", ""factors"": [""..."", ""...""]}";
    }

    public static class Messages
    {
        public const string EmptySymbol = "Stock symbol cannot be empty.";
        public const string SymbolNotFound = "Stock symbol not found. Please check the ticker symbol.";
        public const string OpenAiKeyNotConfigured = "OpenAI API key is not configured. Add your key to appsettings.local.json.";
        public const string WorkflowFailed = "Stock analysis failed. Please try again.";
        public const string UnexpectedError = "An unexpected error occurred.";
        public const string SymbolExists = "This symbol is already in your watchlist.";
        public const string SymbolRemoved = "Symbol removed from watchlist.";
        public const string SymbolAdded = "Symbol added to watchlist.";
    }
}
