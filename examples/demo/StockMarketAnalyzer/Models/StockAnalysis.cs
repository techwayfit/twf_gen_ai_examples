using System.Text.Json.Serialization;

namespace _StockMarketAnalyzer.Models;

public record StockAnalysisResult(
    string Symbol,
    TrendAnalysis Trend,
    StockRecommendation Recommendation,
    RiskAssessment Risk,
    StockQuote Quote,
    List<HistoricalPrice> PriceHistory,
    DateTime AnalyzedAt);

public record TrendAnalysis(
    string Direction,
    string Summary,
    List<string> Indicators);

public record StockRecommendation(
    string Action,
    [property: JsonPropertyName("target_price")] decimal TargetPrice,
    string Reasoning,
    double Confidence);

public record RiskAssessment(
    string Level,
    string Summary,
    List<string> Factors);

public record StageEvent(string Message, int StageIndex, int TotalStages);
