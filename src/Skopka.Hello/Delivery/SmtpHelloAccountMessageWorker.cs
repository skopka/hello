using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Skopka.Hello;

internal sealed partial class SmtpHelloAccountMessageWorker(
    HelloAccountMessageQueue queue,
    SmtpHelloAccountMessageTransport transport,
    ILogger<SmtpHelloAccountMessageWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        await foreach (var message in queue.ReadAllAsync(
            stoppingToken))
        {
            var result = await transport.SendAsync(
                message,
                stoppingToken);
            if (!result.IsSuccess)
            {
                AccountMessageDeliveryFailed(
                    logger,
                    message.Kind,
                    result.Errors.FirstOrDefault()?.Code
                        ?? HelloDeliveryErrorCodes.Failed);
            }
        }
    }

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message =
            "Queued account message delivery failed. Kind: {messageKind}; error code: {errorCode}.")]
    private static partial void AccountMessageDeliveryFailed(
        ILogger logger,
        HelloAccountMessageKind messageKind,
        string errorCode);
}
