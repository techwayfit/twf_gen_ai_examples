using System.Text.Json;
using _StockMarketAnalyzer.Models;

namespace _StockMarketAnalyzer.Services;

/// <summary>
/// Manages the user's stock watchlist with JSON file persistence.
/// </summary>
public class WatchlistService
{
    private readonly string _filePath;
    private readonly ILogger<WatchlistService> _logger;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public WatchlistService(IConfiguration configuration, ILogger<WatchlistService> logger)
    {
        _logger = logger;
        _filePath = configuration["WatchlistFile"] ?? "watchlist.json";

        if (!Path.IsPathRooted(_filePath))
        {
            _filePath = Path.Combine(AppContext.BaseDirectory, _filePath);
        }

        EnsureFileExists();
    }

    /// <summary>
    /// Returns all watchlist items.
    /// </summary>
    public Watchlist GetAll()
    {
        lock (_lock)
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<Watchlist>(json, JsonOpts) ?? new Watchlist();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read watchlist file, returning empty");
                return new Watchlist();
            }
        }
    }

    /// <summary>
    /// Adds a symbol to the watchlist. Returns false if already present.
    /// </summary>
    public bool Add(string symbol, string name = "")
    {
        symbol = symbol.ToUpper().Trim();
        lock (_lock)
        {
            var watchlist = GetAll();
            if (watchlist.Items.Any(i => i.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)))
                return false;

            watchlist.Items.Add(new WatchlistItem
            {
                Symbol = symbol,
                Name = string.IsNullOrWhiteSpace(name) ? symbol : name,
                AddedAt = DateTime.UtcNow,
            });
            watchlist.LastUpdated = DateTime.UtcNow;
            Save(watchlist);
            return true;
        }
    }

    /// <summary>
    /// Removes a symbol from the watchlist. Returns false if not found.
    /// </summary>
    public bool Remove(string symbol)
    {
        symbol = symbol.ToUpper().Trim();
        lock (_lock)
        {
            var watchlist = GetAll();
            var removed = watchlist.Items.RemoveAll(i =>
                i.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)) > 0;

            if (removed)
            {
                watchlist.LastUpdated = DateTime.UtcNow;
                Save(watchlist);
            }
            return removed;
        }
    }

    /// <summary>
    /// Refreshes prices for all watchlist items using Yahoo Finance.
    /// </summary>
    public async Task RefreshPricesAsync(YahooFinanceService yahooService, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var watchlist = GetAll();
            foreach (var item in watchlist.Items)
            {
                try
                {
                    var quote = yahooService.GetQuoteAsync(item.Symbol, ct).GetAwaiter().GetResult();
                    if (quote != null)
                    {
                        item.LastPrice = quote.Price;
                        item.LastChange = quote.Change;
                        item.LastChangePercent = quote.ChangePercent;
                        item.Name = quote.Name;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to refresh price for {Symbol}", item.Symbol);
                }
            }
            watchlist.LastUpdated = DateTime.UtcNow;
            Save(watchlist);
        }
    }

    /// <summary>
    /// Checks if a symbol is in the watchlist.
    /// </summary>
    public bool Contains(string symbol)
    {
        var watchlist = GetAll();
        return watchlist.Items.Any(i =>
            i.Symbol.Equals(symbol.Trim().ToUpper(), StringComparison.OrdinalIgnoreCase));
    }

    private void Save(Watchlist watchlist)
    {
        try
        {
            var json = JsonSerializer.Serialize(watchlist, JsonOpts);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save watchlist to {Path}", _filePath);
        }
    }

    private void EnsureFileExists()
    {
        if (!File.Exists(_filePath))
        {
            var defaultWatchlist = new Watchlist
            {
                LastUpdated = DateTime.UtcNow,
                Items = new List<WatchlistItem>
                {
                    new() { Symbol = "AAPL",  Name = "Apple Inc.",       AddedAt = DateTime.UtcNow },
                    new() { Symbol = "MSFT",  Name = "Microsoft Corp.",  AddedAt = DateTime.UtcNow },
                    new() { Symbol = "GOOGL", Name = "Alphabet Inc.",    AddedAt = DateTime.UtcNow },
                    new() { Symbol = "AMZN",  Name = "Amazon.com Inc.",  AddedAt = DateTime.UtcNow },
                    new() { Symbol = "TSLA",  Name = "Tesla Inc.",       AddedAt = DateTime.UtcNow },
                }
            };

            try
            {
                var json = JsonSerializer.Serialize(defaultWatchlist, JsonOpts);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create default watchlist at {Path}", _filePath);
            }
        }
    }
}
