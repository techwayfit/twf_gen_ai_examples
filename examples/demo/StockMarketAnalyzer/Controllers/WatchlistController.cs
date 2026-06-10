using Microsoft.AspNetCore.Mvc;
using _StockMarketAnalyzer.Models;
using _StockMarketAnalyzer.Services;

namespace _StockMarketAnalyzer.Controllers;

[ApiController]
[Route("api/[controller]")]
[IgnoreAntiforgeryToken]
public class WatchlistController : ControllerBase
{
    private readonly WatchlistService _watchlistService;
    private readonly YahooFinanceService _yahooService;
    private readonly ILogger<WatchlistController> _logger;

    public WatchlistController(
        WatchlistService watchlistService,
        YahooFinanceService yahooService,
        ILogger<WatchlistController> logger)
    {
        _watchlistService = watchlistService;
        _yahooService = yahooService;
        _logger = logger;
    }

    /// <summary>Returns all watchlist items with prices.</summary>
    [HttpGet]
    public IActionResult GetAll()
    {
        var watchlist = _watchlistService.GetAll();
        return Ok(watchlist);
    }

    /// <summary>Adds a symbol to the watchlist.</summary>
    [HttpPost]
    public async Task<IActionResult> Add([FromBody] WatchlistAddRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Symbol))
            return BadRequest(new { error = "Symbol is required." });

        // Fetch the name from Yahoo Finance
        var quote = await _yahooService.GetQuoteAsync(request.Symbol.Trim(), ct);
        var name = quote?.Name ?? request.Symbol.Trim().ToUpper();

        var added = _watchlistService.Add(request.Symbol.Trim(), name);
        if (!added)
            return Conflict(new { error = Constants.Messages.SymbolExists });

        return Ok(new { message = Constants.Messages.SymbolAdded, symbol = request.Symbol.ToUpper() });
    }

    /// <summary>Removes a symbol from the watchlist.</summary>
    [HttpDelete("{symbol}")]
    public IActionResult Remove(string symbol)
    {
        var removed = _watchlistService.Remove(symbol);
        if (!removed)
            return NotFound(new { error = "Symbol not found in watchlist." });

        return Ok(new { message = Constants.Messages.SymbolRemoved, symbol = symbol.ToUpper() });
    }

    /// <summary>Refreshes prices for all watchlist items.</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        await _watchlistService.RefreshPricesAsync(_yahooService, ct);
        var watchlist = _watchlistService.GetAll();
        return Ok(watchlist);
    }

    /// <summary>Checks if a symbol is in the watchlist.</summary>
    [HttpGet("contains/{symbol}")]
    public IActionResult Contains(string symbol)
    {
        var exists = _watchlistService.Contains(symbol);
        return Ok(new { symbol = symbol.ToUpper(), inWatchlist = exists });
    }
}

public class WatchlistAddRequest
{
    public string Symbol { get; set; } = string.Empty;
}
