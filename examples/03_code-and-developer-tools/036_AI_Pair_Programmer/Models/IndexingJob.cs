namespace _036_AI_Pair_Programmer.Models;

public enum IndexingJobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed class IndexingJob
{
    public string JobId { get; set; } = string.Empty;
    public IndexRequest Request { get; set; } = new();
    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public IndexingJobStatus Status { get; set; } = IndexingJobStatus.Queued;
    public IndexingProgress Progress { get; set; } = new();
    public IndexResult? Result { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public CancellationTokenSource CancellationTokenSource { get; set; } = new();
}

public sealed class IndexingProgress
{
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public int TotalChunks { get; set; }
    public int ProcessedChunks { get; set; }
    public string CurrentFile { get; set; } = string.Empty;
    public string CurrentOperation { get; set; } = string.Empty;
    public int PercentComplete => TotalChunks > 0 ? (int)((double)ProcessedChunks / TotalChunks * 100) : 0;
}

public sealed record IndexingJobResponse(
    string JobId,
    IndexingJobStatus Status,
    IndexingProgress Progress,
    IndexResult? Result = null,
    string? ErrorMessage = null);
