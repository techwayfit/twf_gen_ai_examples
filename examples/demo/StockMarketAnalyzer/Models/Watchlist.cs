namespace _StockMarketAnalyzer.Models;

public class Watchlist
{
    public List<WatchlistItem> Items { get; set; } = new();
    public DateTime LastUpdated { get; set; }
}

public class WatchlistItem
{
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal? LastPrice { get; set; }
    public decimal? LastChange { get; set; }
    public decimal? LastChangePercent { get; set; }
    public DateTime AddedAt { get; set; }
}
