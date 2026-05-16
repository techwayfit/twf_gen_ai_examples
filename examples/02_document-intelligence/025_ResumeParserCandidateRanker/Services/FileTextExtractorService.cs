using System.Text;
using UglyToad.PdfPig;

namespace _025_ResumeParserCandidateRanker.Services;

/// <summary>
/// Extracts plain text from uploaded .txt and .pdf resume files.
/// </summary>
public class FileTextExtractorService(IConfiguration configuration)
{
    private long MaxBytes => configuration.GetValue<long>("Upload:MaxFileSizeMb", 10) * 1024 * 1024;

    /// <summary>
    /// Reads <paramref name="stream"/> and returns its text content.
    /// Supports .txt (UTF-8 / BOM-detected) and .pdf (via PdfPig).
    /// </summary>
    public async Task<string> ExtractAsync(Stream stream, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".txt" => await ReadTextAsync(stream),
            ".pdf" => await ReadPdfAsync(stream),
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

    private static async Task<string> ReadPdfAsync(Stream stream)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        var bytes = ms.ToArray();

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
}
