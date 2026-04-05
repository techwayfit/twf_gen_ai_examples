using Cronos;

namespace _003_Automated_NewsLetter.Services;

/// <summary>
/// Background service that runs the newsletter pipeline on a configurable cron schedule.
/// The schedule is read from <c>NewsletterSettings:Schedule</c> (5-field cron, e.g. "0 8 * * 1").
/// The pipeline is re-invoked on each trigger; a no-op Action&lt;string&gt; is used because
/// scheduled runs do not have a live SSE connection.
/// </summary>
public class SchedulerService : BackgroundService
{
    private readonly ILogger<SchedulerService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _cronExpression;

    public SchedulerService(
        ILogger<SchedulerService> logger,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration)
    {
        _logger         = logger;
        _scopeFactory   = scopeFactory;
        _cronExpression = configuration["NewsletterSettings:Schedule"] ?? "0 8 * * 1";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CronExpression cron;
        try
        {
            cron = CronExpression.Parse(_cronExpression, CronFormat.Standard);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invalid cron expression '{Expr}' — scheduler disabled", _cronExpression);
            return;
        }

        _logger.LogInformation("Newsletter scheduler started. Cron: {Expr}", _cronExpression);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now  = DateTimeOffset.UtcNow;
            var next = cron.GetNextOccurrence(now, TimeZoneInfo.Utc);

            if (next is null)
            {
                _logger.LogWarning("Could not compute next cron occurrence — scheduler exiting");
                return;
            }

            var delay = next.Value - now;
            _logger.LogInformation("Next newsletter scheduled at {Next} (in {Delay})", next, delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunScheduledPipelineAsync(stoppingToken);
        }
    }

    private async Task RunScheduledPipelineAsync(CancellationToken ct)
    {
        _logger.LogInformation("Scheduled newsletter pipeline starting");
        try
        {
            await using var scope   = _scopeFactory.CreateAsyncScope();
            var service             = scope.ServiceProvider.GetRequiredService<NewsletterWorkflowService>();

            // No live SSE connection on scheduled runs — discard streamed chunks
            Action<string> noOp = _ => { };
            var result = await service.RunPipelineAsync(noOp, ct);

            if (result.IsSuccess)
                _logger.LogInformation("Scheduled newsletter generated successfully");
            else
                _logger.LogWarning("Scheduled newsletter failed: {Error}", result.ErrorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in scheduled newsletter pipeline");
        }
    }
}
