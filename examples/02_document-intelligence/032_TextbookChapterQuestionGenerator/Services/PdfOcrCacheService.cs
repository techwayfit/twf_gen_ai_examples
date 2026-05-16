using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace _032_TextbookChapterQuestionGenerator.Services;

/// <summary>
/// Persistent SQLite cache for Azure Document Intelligence OCR results.
///
/// Cache key  : SHA-256 hex digest of the raw PDF bytes.
/// Cache value: the extracted plain text returned by Azure.
///
/// Registered as a Singleton so the DB file is opened once per process.
/// </summary>
public sealed class PdfOcrCacheService : IDisposable
{
    private readonly SqliteConnection  _connection;
    private readonly ILogger<PdfOcrCacheService> _logger;
    private readonly SemaphoreSlim     _lock = new(1, 1);

    public PdfOcrCacheService(IConfiguration configuration, ILogger<PdfOcrCacheService> logger)
    {
        _logger = logger;

        var dbPath = configuration["OcrCache:DbPath"]
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "032_qgen",
                "ocr_cache.db");

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        EnsureSchema();

        _logger.LogInformation("PDF OCR cache opened at {Path}", dbPath);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Compute the SHA-256 cache key for a byte array.</summary>
    public static string ComputeKey(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>Returns cached text for <paramref name="key"/>, or null if not found.</summary>
    public async Task<string?> GetAsync(string key)
    {
        await _lock.WaitAsync();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT extracted_text FROM ocr_cache WHERE pdf_hash = $key LIMIT 1;";
            cmd.Parameters.AddWithValue("$key", key);
            var result = await cmd.ExecuteScalarAsync();
            if (result is string text)
            {
                _logger.LogInformation("OCR cache hit for key {Key}", key[..8]);
                return text;
            }
            return null;
        }
        finally { _lock.Release(); }
    }

    /// <summary>Stores OCR text in the cache, replacing any existing entry.</summary>
    public async Task SetAsync(string key, string text, string? fileName = null)
    {
        await _lock.WaitAsync();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ocr_cache (pdf_hash, extracted_text, file_name, cached_at)
                VALUES ($key, $text, $name, $at)
                ON CONFLICT(pdf_hash) DO UPDATE SET
                    extracted_text = excluded.extracted_text,
                    file_name      = excluded.file_name,
                    cached_at      = excluded.cached_at;
                """;
            cmd.Parameters.AddWithValue("$key",  key);
            cmd.Parameters.AddWithValue("$text", text);
            cmd.Parameters.AddWithValue("$name", (object?)fileName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$at",   DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync();
            _logger.LogInformation("OCR result cached for key {Key} ({Chars} chars)", key[..8], text.Length);
        }
        finally { _lock.Release(); }
    }

    // ── Schema ────────────────────────────────────────────────────────────────

    private void EnsureSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS ocr_cache (
                pdf_hash       TEXT    PRIMARY KEY NOT NULL,
                extracted_text TEXT    NOT NULL,
                file_name      TEXT,
                cached_at      TEXT    NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _lock.Dispose();
        _connection.Dispose();
    }
}
