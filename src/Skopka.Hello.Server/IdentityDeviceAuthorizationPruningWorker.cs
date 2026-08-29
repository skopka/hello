using Skopka.Identity.DeviceAuthorization;

namespace Skopka.Hello.Server;

internal sealed class IdentityDeviceAuthorizationPruningWorker<TProfile>(
    IServiceScopeFactory scopeFactory,
    ILogger<IdentityDeviceAuthorizationPruningWorker<TProfile>> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly Action<ILogger, int, Exception?> LogPruned =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(1021, "DeviceAuthorizationRequestsPruned"),
            "Pruned {RequestRowCount} expired device authorization requests.");
    private static readonly Action<ILogger, Exception?> LogPruningFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1022, "DeviceAuthorizationPruningFailed"),
            "Device authorization request pruning failed.");

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var requests = scope.ServiceProvider.GetRequiredService<
                    IIdentityDeviceAuthorizationService<TProfile>>();
                var removed = await requests.PruneAsync(stoppingToken);
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
