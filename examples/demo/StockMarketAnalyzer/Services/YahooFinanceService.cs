using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using _StockMarketAnalyzer.Models;

namespace _StockMarketAnalyzer.Services;

/// <summary>
/// Fetches stock data from Yahoo Finance v8 chart API.
/// Uses IHttpClientFactory with a named "yahoo" client that includes User-Agent header.
/// Includes retry logic for 429 (rate limit) responses.
/// </summary>
public class YahooFinanceService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<YahooFinanceService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private const int MaxRetries = 3;
    private static readonly TimeSpan[] RetryDelays = [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4)
    ];

    public YahooFinanceService(IHttpClientFactory httpClientFactory, ILogger<YahooFinanceService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private HttpClient CreateClient() => _httpClientFactory.CreateClient("yahoo");

    private async Task<HttpResponseMessage> GetWithRetryAsync(string url, CancellationToken ct)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            var client = CreateClient();
            var response = await client.GetAsync(url, ct);

            if (response.StatusCode == (HttpStatusCode)429 && attempt < MaxRetries - 1)
            {
                _logger.LogDebug("Rate limited (429) on attempt {Attempt}, retrying after {Delay}s",
                    attempt + 1, RetryDelays[attempt].TotalSeconds);
                await Task.Delay(RetryDelays[attempt], ct);
                response.Dispose();
                continue;
            }

            return response;
        }

        // Should not reach here, but just in case
        return await CreateClient().GetAsync(url, ct);
    }

    /// <summary>
    /// Gets a real-time quote for a stock symbol.
    /// </summary>
    public async Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval=1d&range=1d";
            using var response = await GetWithRetryAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var chart = JsonSerializer.Deserialize<YahooChartResponse>(json, JsonOpts);

            var result = chart?.Chart?.Result?.FirstOrDefault();
            if (result?.Meta == null) return null;

            var meta = result.Meta;
            var price = (decimal)meta.RegularMarketPrice;
            var previousClose = (decimal)(meta.PreviousClose ?? meta.ChartPreviousClose ?? meta.RegularMarketPrice);
            var change = price - previousClose;
            var changePercent = previousClose != 0 ? Math.Round(change / previousClose * 100, 2) : 0;
            var symbolName = meta.ShortName ?? meta.Symbol ?? symbol;
            var volumeValue = (decimal)meta.RegularMarketVolume;

            return new StockQuote(
                Symbol: symbol.ToUpper(),
                Name: symbolName,
                Price: price,
                Change: Math.Round(change, 2),
                ChangePercent: changePercent,
                Volume: volumeValue);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch quote for {Symbol}", symbol);
            return null;
        }
    }

    /// <summary>
    /// Gets historical daily price data for a stock symbol.
    /// </summary>
    public async Task<List<HistoricalPrice>> GetHistoryAsync(string symbol, int months = 3, CancellationToken ct = default)
    {
        try
        {
            var range = months switch
            {
                <= 1 => "1mo",
                <= 3 => "3mo",
                <= 6 => "6mo",
                <= 12 => "1y",
                _ => "2y"
            };

            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval=1d&range={range}";
            _logger.LogInformation("Fetching history from: {Url}", url);

            using var response = await GetWithRetryAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            _logger.LogDebug("History response length: {Length}", json.Length);

            var chart = JsonSerializer.Deserialize<YahooChartResponse>(json, JsonOpts);

            var result = chart?.Chart?.Result?.FirstOrDefault();

            _logger.LogInformation("History parse results - Chart: {HasChart}, Result: {HasResult}, Timestamps: {HasTimestamps}, Indicators: {HasIndicators}", 
                chart?.Chart != null, 
                result != null,
                result?.Timestamps != null,
                result?.Indicators?.Quote != null);

            if (result?.Timestamps == null || result.Indicators?.Quote == null || result.Indicators.Quote.Count == 0)
            {
                _logger.LogWarning("History response has no data for {Symbol}. Timestamps: {Timestamps}, Indicators: {Indicators}", 
                    symbol, 
                    result?.Timestamps?.Count ?? 0,
                    result?.Indicators?.Quote?.Count ?? 0);
                return new List<HistoricalPrice>();
            }

            var timestamps = result.Timestamps;
            var quote = result.Indicators.Quote[0];
            var history = new List<HistoricalPrice>();

            for (int i = 0; i < timestamps.Count; i++)
            {
                if (quote.Open?[i] == null || quote.Close?[i] == null ||
                    quote.High?[i] == null || quote.Low?[i] == null)
                    continue;

                history.Add(new HistoricalPrice(
                    Date: DateTimeOffset.FromUnixTimeSeconds(timestamps[i]).DateTime,
                    Open: (decimal)quote.Open[i]!,
                    High: (decimal)quote.High[i]!,
                    Low: (decimal)quote.Low[i]!,
                    Close: (decimal)quote.Close[i]!,
                    Volume: (decimal)(quote.Volume?[i] ?? 0)));
            }

            _logger.LogInformation("Successfully fetched {Count} history data points for {Symbol}", history.Count, symbol);
            return history;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch history for {Symbol}", symbol);
            return new List<HistoricalPrice>();
        }
    }

    /// <summary>
    /// Searches for stock symbols matching a query string.
    /// Uses Yahoo Finance v1 search API with retry logic.
    /// </summary>
    public async Task<List<StockSearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://query2.finance.yahoo.com/v1/finance/search?q={Uri.EscapeDataString(query)}&quotesCount=10&newsCount=0&listsCount=0";
            using var response = await GetWithRetryAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);

            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var searchResponse = JsonSerializer.Deserialize<YahooSearchResponse>(json, opts);

            return searchResponse?.Quotes?
                .Where(q => q.QuoteType == "EQUITY" || q.QuoteType == "ETF")
                .Select(q => new StockSearchResult(
                    Symbol: q.Symbol ?? string.Empty,
                    Name: q.ShortName ?? q.LongName ?? q.Symbol ?? string.Empty,
                    Exchange: q.Exchange ?? string.Empty,
                    Type: q.QuoteType ?? string.Empty))
                .ToList() ?? new List<StockSearchResult>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search for query: {Query}", query);
            return new List<StockSearchResult>();
        }
    }
}

// ── Yahoo Finance API response DTOs ──────────────────────────────────────────

file class YahooChartResponse
{
    public YahooChart? Chart { get; set; }
}

file class YahooChart
{
    public List<YahooChartResult>? Result { get; set; }
    public object? Error { get; set; }
}

file class YahooChartResult
{
    public YahooMeta? Meta { get; set; }

    [JsonPropertyName("timestamp")]
    public List<long>? Timestamps { get; set; }

    [JsonPropertyName("indicators")]
    public YahooIndicators? Indicators { get; set; }
}

file class YahooMeta
{
    public string? Symbol { get; set; }
    public string? ShortName { get; set; }
    public double RegularMarketPrice { get; set; }
    public double? PreviousClose { get; set; }
    public double? ChartPreviousClose { get; set; }
    public double RegularMarketVolume { get; set; }
}

file class YahooIndicators
{
    [JsonPropertyName("quote")]
    public List<YahooQuote>? Quote { get; set; }
}

file class YahooQuote
{
    [JsonPropertyName("open")]
    public List<double?>? Open { get; set; }

    [JsonPropertyName("high")]
    public List<double?>? High { get; set; }

    [JsonPropertyName("low")]
    public List<double?>? Low { get; set; }

    [JsonPropertyName("close")]
    public List<double?>? Close { get; set; }

    [JsonPropertyName("volume")]
    public List<double?>? Volume { get; set; }
}

// ── Yahoo Finance Search API DTOs ──────────────────────────────────────────

file class YahooSearchResponse
{
    public List<YahooSearchQuote>? Quotes { get; set; }
}

file class YahooSearchQuote
{
    public string? Symbol { get; set; }
    public string? ShortName { get; set; }
    public string? LongName { get; set; }
    public string? Exchange { get; set; }
    public string? QuoteType { get; set; }
}
