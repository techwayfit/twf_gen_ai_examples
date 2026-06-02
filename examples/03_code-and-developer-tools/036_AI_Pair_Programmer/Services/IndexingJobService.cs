using _036_AI_Pair_Programmer.Models;
using System.Collections.Concurrent;

namespace _036_AI_Pair_Programmer.Services;

public sealed class IndexingJobService
{
    private readonly ConcurrentDictionary<string, IndexingJob> _jobs = new();
    private readonly ConcurrentQueue<string> _jobQueue = new();
    private readonly ILogger<IndexingJobService> _logger;

    public IndexingJobService(ILogger<IndexingJobService> logger)
    {
        _logger = logger;
    }

    public string CreateJob(IndexRequest request, string apiKey, string endpoint)
    {
        var jobId = Guid.NewGuid().ToString("N");
        var job = new IndexingJob
        {
            JobId = jobId,
            Request = request,
            ApiKey = apiKey,
            Endpoint = endpoint,
            Status = IndexingJobStatus.Queued,
            CreatedAt = DateTime.UtcNow
        };

        if (_jobs.TryAdd(jobId, job))
        {
            _jobQueue.Enqueue(jobId);
            _logger.LogInformation("Created indexing job {JobId} for repository {RepoPath}", jobId, request.RepoPath);
            return jobId;
        }

        throw new InvalidOperationException($"Failed to create job with ID {jobId}");
    }

    public bool TryDequeueJob(out string? jobId)
    {
        return _jobQueue.TryDequeue(out jobId);
    }

    public IndexingJob? GetJob(string jobId)
    {
        _jobs.TryGetValue(jobId, out var job);
        return job;
    }

    public IndexingJobResponse? GetJobStatus(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            return null;
        }

        return new IndexingJobResponse(
            job.JobId,
            job.Status,
            job.Progress,
            job.Result,
            job.ErrorMessage);
    }

    public void UpdateJobStatus(string jobId, IndexingJobStatus status, string? errorMessage = null)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = status;
            job.ErrorMessage = errorMessage;

            if (status == IndexingJobStatus.Running && !job.StartedAt.HasValue)
            {
                job.StartedAt = DateTime.UtcNow;
            }
            else if (status is IndexingJobStatus.Completed or IndexingJobStatus.Failed or IndexingJobStatus.Cancelled)
            {
                job.CompletedAt = DateTime.UtcNow;
            }

            _logger.LogInformation("Job {JobId} status updated to {Status}", jobId, status);
        }
    }

    public void UpdateJobProgress(string jobId, Action<IndexingProgress> updateAction)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            updateAction(job.Progress);
        }
    }

    public void SetJobResult(string jobId, IndexResult result)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Result = result;
            job.Status = IndexingJobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            _logger.LogInformation("Job {JobId} completed successfully with {ChunkCount} chunks", jobId, result.ChunkCount);
        }
    }

    public void CancelJob(string jobId)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.CancellationTokenSource.Cancel();
            job.Status = IndexingJobStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
            _logger.LogInformation("Job {JobId} cancelled", jobId);
        }
    }

    public void CleanupOldJobs(TimeSpan age)
    {
        var cutoff = DateTime.UtcNow - age;
        var oldJobs = _jobs
            .Where(kvp => kvp.Value.CompletedAt.HasValue && kvp.Value.CompletedAt.Value < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var jobId in oldJobs)
        {
            if (_jobs.TryRemove(jobId, out var job))
            {
                job.CancellationTokenSource.Dispose();
                _logger.LogDebug("Cleaned up old job {JobId}", jobId);
            }
        }
    }

    public IEnumerable<IndexingJobResponse> GetAllJobs()
    {
        return _jobs.Values
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new IndexingJobResponse(
                j.JobId,
                j.Status,
                j.Progress,
                j.Result,
                j.ErrorMessage));
    }
}
