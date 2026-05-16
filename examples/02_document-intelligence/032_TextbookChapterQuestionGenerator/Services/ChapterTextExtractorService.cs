using System.Text;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using UglyToad.PdfPig;

namespace _032_TextbookChapterQuestionGenerator.Services;

/// <summary>
/// Extracts plain text from uploaded .txt and .pdf chapter files.
///
/// PDF strategy:
///   1. PdfPig extracts the embedded text layer (native PDFs — fast, free).
///   2. If the result is suspiciously sparse (image-based / scanned PDF),
///      checks the SQLite OCR cache by SHA-256 key before calling Azure.
///   3. If no cache hit, calls Azure AI Document Intelligence "prebuilt-read"
///      and stores the result in the cache for future uploads.
///   4. If Azure DI is not configured, throws a descriptive exception so the
///      widget can surface a helpful message to the user.
/// </summary>
public class ChapterTextExtractorService(
    IConfiguration                       configuration,
    PdfOcrCacheService                   ocrCache,
    ILogger<ChapterTextExtractorService> logger)
{
    // Threshold: fewer than this many characters per page → treat as image PDF.
    private const int MinCharsPerPageThreshold = 30;

    public async Task<string> ExtractAsync(Stream stream, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".txt" => await ReadTextAsync(stream),
            ".pdf" => await ReadPdfAsync(stream, fileName),
            _      => throw new NotSupportedException(
                          $"File type '{ext}' is not supported. Only .txt and .pdf are accepted."),
        };
    }

    private static async Task<string> ReadTextAsync(Stream stream)
    {
        using var reader = new StreamReader(
            stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private async Task<string> ReadPdfAsync(Stream stream, string fileName)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        var bytes = ms.ToArray();

        // ── Step 1: try PdfPig (native text layer) ───────────────────────────
        var nativeText = ExtractNativeText(bytes);

        var pageCount = GetPageCount(bytes);
        var avgCharsPerPage = pageCount > 0
            ? (double)nativeText.Length / pageCount
            : nativeText.Length;

        if (avgCharsPerPage >= MinCharsPerPageThreshold)
        {
            logger.LogInformation("Native text extraction succeeded ({Pages} pages, {Chars} chars)",
                pageCount, nativeText.Length);
            return nativeText;
        }

        // ── Step 2: check SQLite OCR cache ────────────────────────────────────
        logger.LogInformation(
            "PDF appears image-based (avg {Avg:F0} chars/page) — checking OCR cache",
            avgCharsPerPage);

        var cacheKey = PdfOcrCacheService.ComputeKey(bytes);
        var cached   = await ocrCache.GetAsync(cacheKey);
        if (cached is not null)
            return cached;

        // ── Step 3: fall back to Azure Document Intelligence OCR ─────────────
        logger.LogInformation("No cache hit — calling Azure Document Intelligence OCR");

        var ocrText = await OcrWithAzureAsync(bytes);
        await ocrCache.SetAsync(cacheKey, ocrText, fileName);
        return ocrText;
    }

    private static string ExtractNativeText(byte[] bytes)
    {
        using var pdf = PdfDocument.Open(bytes);
        var sb = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            var text = page.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine(text);
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    private static int GetPageCount(byte[] bytes)
    {
        try
        {
            using var pdf = PdfDocument.Open(bytes);
            return pdf.NumberOfPages;
        }
        catch { return 1; }
    }

    private async Task<string> OcrWithAzureAsync(byte[] bytes)
    {
        var endpoint = configuration["AzureDocumentIntelligence:Endpoint"];
        var apiKey   = configuration["AzureDocumentIntelligence:ApiKey"];

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey)
            || endpoint.Contains("your-resource"))
        {
            throw new InvalidOperationException(
                "This PDF has no text layer (scanned / image-based). " +
                "Azure Document Intelligence is required for OCR but is not configured. " +
                "Add AzureDocumentIntelligence:Endpoint and AzureDocumentIntelligence:ApiKey " +
                "to appsettings.local.json.");
        }

        var client = new DocumentAnalysisClient(
            new Uri(endpoint),
            new AzureKeyCredential(apiKey));

        using var pdfStream = new MemoryStream(bytes);
        var operation = await client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            "prebuilt-read",
            pdfStream);

        var result = operation.Value;

        var sb = new StringBuilder();
        foreach (var page in result.Pages)
        {
            foreach (var line in page.Lines)
            {
                sb.AppendLine(line.Content);
            }
            sb.AppendLine();
        }

        logger.LogInformation("Azure DI OCR complete — extracted {Chars} chars from {Pages} pages",
            sb.Length, result.Pages.Count);

        return sb.ToString();
    }
}
