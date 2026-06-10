namespace _StockMarketAnalyzer.Models;

public record StockQuote(
    string Symbol,
    string Name,
    decimal Price,
    decimal Change,
    decimal ChangePercent,
    decimal Volume);

public record HistoricalPrice(
    DateTime Date,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume);

public record StockSearchResult(
    string Symbol,
    string Name,
    string Exchange,
    string Type);
