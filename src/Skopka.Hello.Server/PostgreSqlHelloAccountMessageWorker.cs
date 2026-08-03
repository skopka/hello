using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.Server;

internal sealed partial class PostgreSqlHelloAccountMessageWorker
    : BackgroundService
{
    private readonly PostgreSqlHelloAccountMessageOutbox outbox;
    private readonly HelloServerPersistenceOptions options;
    private readonly ILogger<PostgreSqlHelloAccountMessageWorker> logger;
    private readonly Dictionary<
        string,
        IHelloAccountMessageProvider> providers;

    public PostgreSqlHelloAccountMessageWorker(
        PostgreSqlHelloAccountMessageOutbox outbox,
        IEnumerable<IHelloAccountMessageProvider> providers,
        HelloServerPersistenceOptions options,
        HelloDurableEmailRouteOptions route,
        ILogger<PostgreSqlHelloAccountMessageWorker> logger)
    {
        this.outbox = outbox;
        this.options = options;
        this.logger = logger;
        this.providers = providers
            .Where(provider => provider is not
                PostgreSqlHelloAccountMessageOutbox)
            .ToDictionary(
                provider => HelloAccountMessageDispatcher
                    .NormalizeProviderId(
                        provider.ProviderId,
                        "A delivery provider id"),
                StringComparer.OrdinalIgnoreCase);
        if (!this.providers.TryGetValue(
            route.DestinationProviderId,
            out var destinationProvider)
            || destinationProvider.Channel != HelloDeliveryChannel.Email)
        {
            throw new InvalidOperationException(
                $"Durable email destination provider '{route.DestinationProviderId}' is not registered for Email.");
        }
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            HelloAccountMessageOutboxLease? lease;
            try
            {
                lease = await outbox.TryLeaseAsync(stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (NpgsqlException)
            {
                OutboxLeaseFailed(logger, null);
                HelloServerDiagnostics.PersistenceFailures.Add(
                    1,
                    new KeyValuePair<string, object?>(
                        "operation",
                        "message.lease"));
                await DelayAsync(stoppingToken);
                continue;
            }
            catch (Exception)
            {
                OutboxLeaseFailed(logger, null);
                HelloServerDiagnostics.PersistenceFailures.Add(
                    1,
                    new KeyValuePair<string, object?>(
                        "operation",
                        "message.lease"));
                await DelayAsync(stoppingToken);
                continue;
            }

            if (lease is null)
            {
                await DelayAsync(stoppingToken);
                continue;
            }

            var errorCode = await DeliverAsync(
                lease,
                stoppingToken);
            OperationResult finished;
            try
            {
                finished = errorCode is null
                    ? await outbox.CompleteAsync(lease, stoppingToken)
                    : await outbox.FailAsync(
                        lease,
                        errorCode,
                        stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                OutboxFinishFailed(
                    logger,
                    lease.Message.MessageId,
                    lease.Message.Kind,
                    HelloDeliveryErrorCodes.Failed,
                    null);
                continue;
            }
            if (!finished.IsSuccess)
            {
                OutboxFinishFailed(
                    logger,
                    lease.Message.MessageId,
                    lease.Message.Kind,
                    finished.Errors.FirstOrDefault()?.Code
                        ?? HelloDeliveryErrorCodes.Failed,
                    null);
            }
        }
    }

    private async Task<string?> DeliverAsync(
        HelloAccountMessageOutboxLease lease,
        CancellationToken cancellationToken)
    {
        if (!providers.TryGetValue(
            lease.DestinationProviderId,
            out var provider)
            || provider.Channel != lease.Message.Channel)
        {
            return HelloDeliveryErrorCodes.NotConfigured;
        }

        try
        {
            var result = await provider.SendAsync(
                lease.Message,
                cancellationToken);
            if (result.IsSuccess)
            {
                return null;
            }

            var errorCode = result.Errors.FirstOrDefault()?.Code
                ?? HelloDeliveryErrorCodes.Failed;
            AccountMessageDeliveryFailed(
                logger,
                lease.DestinationProviderId,
                lease.Message.MessageId,
                lease.Message.Kind,
                errorCode,
                null);
            return errorCode;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            AccountMessageDeliveryFailed(
                logger,
                lease.DestinationProviderId,
                lease.Message.MessageId,
                lease.Message.Kind,
                HelloDeliveryErrorCodes.Failed,
                null);
            return HelloDeliveryErrorCodes.Failed;
        }
    }

    private async Task DelayAsync(CancellationToken cancellationToken)
    {
        if (options.PollingInterval == TimeSpan.Zero)
        {
            await Task.Yield();
            return;
        }

        await Task.Delay(options.PollingInterval, cancellationToken);
    }

    [LoggerMessage(
        EventId = 2121,
        Level = LogLevel.Error,
        Message = "Could not lease a durable account message.")]
    private static partial void OutboxLeaseFailed(
        ILogger logger,
        Exception? exception);

    [LoggerMessage(
        EventId = 2122,
        Level = LogLevel.Error,
        Message = "Durable account-message delivery failed. Provider: {providerId}; message: {messageId}; kind: {messageKind}; error code: {errorCode}.")]
    private static partial void AccountMessageDeliveryFailed(
        ILogger logger,
        string providerId,
        Guid messageId,
        HelloAccountMessageKind messageKind,
        string errorCode,
        Exception? exception);

    [LoggerMessage(
        EventId = 2123,
        Level = LogLevel.Error,
        Message = "Could not finish durable account message {messageId}; kind: {messageKind}; error code: {errorCode}.")]
    private static partial void OutboxFinishFailed(
        ILogger logger,
        Guid messageId,
        HelloAccountMessageKind messageKind,
        string errorCode,
        Exception? exception);
}
