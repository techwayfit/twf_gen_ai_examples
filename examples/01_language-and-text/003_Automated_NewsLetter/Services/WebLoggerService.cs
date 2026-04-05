using Microsoft.Extensions.Logging;

namespace _003_Automated_NewsLetter.Services;

/// <summary>
/// Captures application log entries in memory so the UI can display them in real time.
/// Registered as both a singleton service (for injection into Blazor components) and an
/// ILoggerProvider (so the .NET logging pipeline writes into it automatically).
/// </summary>
public class WebLoggerService : ILoggerProvider
{
    private const int MaxEntries = 300;

    private readonly List<WebLogEntry> _entries = new();
    private readonly Lock _lock = new();

    /// <summary>Raised on the thread that added the entry; components must marshal to the UI thread.</summary>
    public event Action? OnNewEntry;

    public IReadOnlyList<WebLogEntry> Entries
    {
        get { lock (_lock) return _entries.ToList(); }
    }

    public void Clear()
    {
        lock (_lock) _entries.Clear();
        OnNewEntry?.Invoke();
    }

    // ── ILoggerProvider ───────────────────────────────────────────────────────

    public ILogger CreateLogger(string categoryName) => new WebLogger(this, categoryName);

    public void Dispose() { }

    // ── Internal write path ───────────────────────────────────────────────────

    internal void AddEntry(WebLogEntry entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
                _entries.RemoveAt(0);
        }
        OnNewEntry?.Invoke();
    }
}

// ── Log entry record ──────────────────────────────────────────────────────────

public record WebLogEntry(
    DateTime    Timestamp,
    LogLevel    Level,
    string      Category,
    string      Message);

// ── ILogger implementation ────────────────────────────────────────────────────

internal sealed class WebLogger : ILogger
{
    private readonly WebLoggerService _service;
    private readonly string           _category;

    // Only capture logs from our own namespaces to avoid ASP.NET framework noise.
    private static readonly string[] AppPrefixes =
    [
        "_003_Automated_NewsLetter",
        "TwfAiFramework"
    ];

    public WebLogger(WebLoggerService service, string category)
    {
        _service  = service;
        _category = category;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) =>
        logLevel >= LogLevel.Information &&
        AppPrefixes.Any(p => _category.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    public void Log<TState>(
        LogLevel                        logLevel,
        EventId                         eventId,
        TState                          state,
        Exception?                      exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var shortCategory = _category.Split('.').Last();
        var message       = formatter(state, exception);
        if (exception is not null)
            message += $" | {exception.GetType().Name}: {exception.Message}";

        _service.AddEntry(new WebLogEntry(DateTime.UtcNow, logLevel, shortCategory, message));
    }
}
