using Skopka.Hello.AuthorizationServer;

namespace Skopka.Hello.Server;

internal sealed class HelloNativeAppPruningWorker(
    IServiceScopeFactory scopes,
    HelloNativeAppSignInOptions options,
    ILogger<HelloNativeAppPruningWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IHelloNativeAppFlowStore>();
                await store.PruneAsync(DateTimeOffset.UtcNow - options.RetentionAfterExpiration,
                    options.CleanupBatchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogPruningFailed(logger, exception);
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private static readonly Action<ILogger, Exception?> LogPruningFailed =
        LoggerMessage.Define(LogLevel.Warning, new EventId(1023, "NativeAppPruningFailed"),
            "Native app sign-in cleanup failed.");
}
