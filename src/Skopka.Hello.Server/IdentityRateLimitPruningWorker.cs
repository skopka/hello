using Skopka.Identity.RateLimiting;

namespace Skopka.Hello.Server;

internal sealed class IdentityRateLimitPruningWorker<TProfile>(
    IServiceScopeFactory scopeFactory,
    ILogger<IdentityRateLimitPruningWorker<TProfile>> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval =
        TimeSpan.FromHours(1);
    private static readonly Action<ILogger, int, Exception?>
        LogPruned = LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(1011, "IdentityRateLimitBucketsPruned"),
            "Pruned {RateLimitBucketCount} expired identity rate-limit buckets.");
    private static readonly Action<ILogger, Exception?>
        LogPruningFailed = LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1012, "IdentityRateLimitPruningFailed"),
            "Identity rate-limit pruning failed.");

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope =
                    scopeFactory.CreateAsyncScope();
                var rateLimiter = scope.ServiceProvider
                    .GetRequiredService<
                        IIdentityRateLimiter<TProfile>>();
                var removed = await rateLimiter.PruneAsync(
                    stoppingToken);
                LogPruned(logger, removed, null);
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
