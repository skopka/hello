using Skopka.Identity.Sessions;

namespace Skopka.Hello.Server;

internal sealed class IdentitySessionPruningWorker<TProfile>(
    IServiceScopeFactory scopeFactory,
    ILogger<IdentitySessionPruningWorker<TProfile>> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval =
        TimeSpan.FromHours(1);
    private static readonly Action<ILogger, int, Exception?>
        LogPruned = LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(1001, "IdentitySessionsPruned"),
            "Pruned {SessionRowCount} expired identity session rows.");
    private static readonly Action<ILogger, Exception?>
        LogPruningFailed = LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1002, "IdentitySessionPruningFailed"),
            "Identity session pruning failed.");

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
                var sessions = scope.ServiceProvider
                    .GetRequiredService<
                        IIdentitySessionService<TProfile>>();
                var removed = await sessions.PruneAsync(
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
