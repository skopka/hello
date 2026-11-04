using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Skopka.Hello;

internal sealed partial class SmtpHelloAccountMessageWorker(
    SmtpHelloAccountMessageQueue queue,
    SmtpHelloAccountMessageTransport transport,
    HelloSmtpOptions options,
    ILogger<SmtpHelloAccountMessageWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        await foreach (var message in queue.ReadAllAsync(
            stoppingToken))
        {
            if (message.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                AccountMessageExpired(
                    logger,
                    options.ProviderId,
                    message.MessageId,
                    message.Kind);
                continue;
            }

            for (var attempt = 0; attempt < 3; attempt++)
            {
                string errorCode;
                try
                {
                    var result = await transport.SendAsync(message, stoppingToken);
                    if (result.IsSuccess) break;
                    errorCode = result.Errors.Any(error => error.Code == HelloEmailTransportErrorCodes.Permanent)
                        ? HelloEmailTransportErrorCodes.Permanent
                        : HelloEmailTransportErrorCodes.Retryable;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception)
                {
                    errorCode = HelloEmailTransportErrorCodes.Retryable;
                }

                AccountMessageDeliveryFailed(logger, options.ProviderId, message.MessageId, message.Kind, errorCode);
                if (errorCode == HelloEmailTransportErrorCodes.Permanent || attempt == 2)
                    break;
                var delay = TimeSpan.FromSeconds(1 << attempt);
                if (message.ExpiresAt <= DateTimeOffset.UtcNow + delay)
                    break;
                await Task.Delay(delay, stoppingToken);
            }
        }
    }

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message =
            "Queued account message delivery failed. Provider: {providerId}; message: {messageId}; kind: {messageKind}; error code: {errorCode}.")]
    private static partial void AccountMessageDeliveryFailed(
        ILogger logger,
        string providerId,
        Guid messageId,
        HelloAccountMessageKind messageKind,
        string errorCode);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Warning,
        Message =
            "Expired queued account message was skipped. Provider: {providerId}; message: {messageId}; kind: {messageKind}.")]
    private static partial void AccountMessageExpired(
        ILogger logger,
        string providerId,
        Guid messageId,
        HelloAccountMessageKind messageKind);
}
