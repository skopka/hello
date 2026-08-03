using System.Diagnostics;
using Skopka.Identity.RateLimiting;

namespace Skopka.Hello.Server;

internal sealed class IdentityRateLimitPruningWorker<TProfile>(
    IServiceScopeFactory scopeFactory,
    IdentityRateLimitOptions rateLimitOptions,
    IdentityRateLimitPruningOptions options,
    ILogger<IdentityRateLimitPruningWorker<TProfile>> logger)
    : BackgroundService
{
    private static readonly Action<ILogger, int, int, long, Exception?>
        LogPruned = LoggerMessage.Define<int, int, long>(
            LogLevel.Information,
            new EventId(1011, "IdentityRateLimitBucketsPruned"),
            "Pruned {RateLimitBucketCount} expired identity rate-limit buckets in {BatchCount} batches and {ElapsedMilliseconds} ms.");
    private static readonly Action<ILogger, int, int, Exception?>
        LogBudgetExhausted = LoggerMessage.Define<int, int>(
            LogLevel.Warning,
            new EventId(1013, "IdentityRateLimitPruningBudgetExhausted"),
            "Identity rate-limit pruning stopped after {BatchCount} batches with {RateLimitBucketCount} rows removed; another full batch may remain.");
    private static readonly Action<ILogger, Exception?>
        LogPruningFailed = LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1012, "IdentityRateLimitPruningFailed"),
            "Identity rate-limit pruning failed.");

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        options.Validate();
        using var timer = new PeriodicTimer(options.Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope =
                    scopeFactory.CreateAsyncScope();
                var rateLimiter = scope.ServiceProvider
                    .GetRequiredService<
                        IIdentityRateLimiter<TProfile>>();
                var stopwatch = Stopwatch.StartNew();
                var totalRemoved = 0;
                var batchCount = 0;
                var removed = 0;
                do
                {
                    removed = await rateLimiter.PruneAsync(
                        stoppingToken);
                    totalRemoved += removed;
                    batchCount++;
                }
                while (removed >= rateLimitOptions.CleanupBatchSize
                    && batchCount < options.MaximumBatchesPerRun
                    && stopwatch.Elapsed < options.TimeBudget);

                LogPruned(
                    logger,
                    totalRemoved,
                    batchCount,
                    stopwatch.ElapsedMilliseconds,
                    null);
                if (removed >= rateLimitOptions.CleanupBatchSize
                    && (batchCount >= options.MaximumBatchesPerRun
                        || stopwatch.Elapsed >= options.TimeBudget))
                {
                    LogBudgetExhausted(
                        logger,
                        batchCount,
                        totalRemoved,
                        null);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogPruningFailed(logger, exception);
            }
        }
    }
}

internal sealed class IdentityRateLimitPruningOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    public int MaximumBatchesPerRun { get; set; } = 20;

    public TimeSpan TimeBudget { get; set; } = TimeSpan.FromSeconds(30);

    public void Validate()
    {
        if (Interval <= TimeSpan.Zero
            || MaximumBatchesPerRun <= 0
            || TimeBudget <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Rate-limit pruning interval, batch count and time budget must be positive.");
        }
    }
}
