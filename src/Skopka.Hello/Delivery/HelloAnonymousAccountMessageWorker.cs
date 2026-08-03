using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Skopka.Hello;

internal sealed partial class HelloAnonymousAccountMessageWorker<TProfile>(
    HelloAnonymousAccountMessageQueue<TProfile> queue,
    IServiceScopeFactory scopeFactory,
    ILogger<HelloAnonymousAccountMessageWorker<TProfile>> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        await foreach (var request in queue.ReadAllAsync(
            stoppingToken))
        {
            try
            {
                await using var scope =
                    scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider
                    .GetRequiredService<
                        HelloAnonymousAccountMessageProcessor<TProfile>>();
                var result = await processor.ProcessAsync(
                    request,
                    stoppingToken);
                if (!result.IsSuccess)
                {
                    AccountMessageProcessingFailed(
                        logger,
                        request.MessageId,
                        request.Kind,
                        result.Errors.FirstOrDefault()?.Code
                            ?? HelloDeliveryErrorCodes.Failed);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                AccountMessageProcessingFailed(
                    logger,
                    request.MessageId,
                    request.Kind,
                    HelloDeliveryErrorCodes.Failed);
            }
        }
    }

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Warning,
        Message =
            "Queued anonymous account-message processing failed. Message: {messageId}; kind: {messageKind}; error code: {errorCode}.")]
    private static partial void AccountMessageProcessingFailed(
        ILogger logger,
        Guid messageId,
        HelloAccountMessageKind messageKind,
        string errorCode);
}
