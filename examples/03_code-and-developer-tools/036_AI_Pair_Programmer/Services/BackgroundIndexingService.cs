using _036_AI_Pair_Programmer.Models;

namespace _036_AI_Pair_Programmer.Services;

public sealed class BackgroundIndexingService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BackgroundIndexingService> _logger;

    public BackgroundIndexingService(
        IServiceProvider serviceProvider,
        ILogger<BackgroundIndexingService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background indexing service started");

        // Start a cleanup timer to remove old completed jobs
        _ = Task.Run(async () => await CleanupLoopAsync(stoppingToken), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextJobAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing indexing job");
            }

            // Small delay to prevent tight loop when no jobs are available
            await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken);
        }

        _logger.LogInformation("Background indexing service stopped");
    }

    private async Task ProcessNextJobAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<IndexingJobService>();
        var workflowService = scope.ServiceProvider.GetRequiredService<CodeIndexingWorkflowService>();

        if (!jobService.TryDequeueJob(out var jobId) || string.IsNullOrEmpty(jobId))
        {
            return;
        }

        var job = jobService.GetJob(jobId);
        if (job == null)
        {
            _logger.LogWarning("Job {JobId} not found in job store", jobId);
            return;
        }

        _logger.LogInformation("Starting processing of job {JobId}", jobId);
        jobService.UpdateJobStatus(jobId, IndexingJobStatus.Running);

        try
        {
            // Create a linked cancellation token that respects both the job cancellation and the service stopping
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                job.CancellationTokenSource.Token, 
                stoppingToken);

            var progressCallback = new Progress<(string operation, string? currentFile, int processedChunks, int totalChunks)>(
                progress =>
                {
                    jobService.UpdateJobProgress(jobId, p =>
                    {
                        p.CurrentOperation = progress.operation;
                        p.CurrentFile = progress.currentFile ?? string.Empty;
                        p.ProcessedChunks = progress.processedChunks;
                        p.TotalChunks = progress.totalChunks;
                    });
                });

            var result = await workflowService.RunAsync(
                job.Request,
                job.ApiKey,
                progressCallback,
                linkedCts.Token);

            jobService.SetJobResult(jobId, result);
            _logger.LogInformation("Job {JobId} completed successfully", jobId);
        }
        catch (OperationCanceledException)
        {
            jobService.UpdateJobStatus(jobId, IndexingJobStatus.Cancelled, "Job was cancelled");
            _logger.LogInformation("Job {JobId} was cancelled", jobId);
        }
        catch (Exception ex)
        {
            jobService.UpdateJobStatus(jobId, IndexingJobStatus.Failed, ex.Message);
            _logger.LogError(ex, "Job {JobId} failed", jobId);
        }
    }

    private async Task CleanupLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Wait 10 minutes between cleanup cycles
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);

                using var scope = _serviceProvider.CreateScope();
                var jobService = scope.ServiceProvider.GetRequiredService<IndexingJobService>();

                // Remove jobs older than 1 hour
                jobService.CleanupOldJobs(TimeSpan.FromHours(1));
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during job cleanup");
            }
        }
    }
}
