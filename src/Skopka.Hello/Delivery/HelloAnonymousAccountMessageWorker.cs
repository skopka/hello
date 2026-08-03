using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Skopka.Hello;

internal sealed partial class HelloAnonymousAccountMessageWorker<TProfile>(
    IHelloAnonymousAccountMessageInbox queue,
    IServiceScopeFactory scopeFactory,
    ILogger<HelloAnonymousAccountMessageWorker<TProfile>> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        await foreach (var lease in queue.ReadAllAsync(
            stoppingToken))
        {
            var request = lease.Request;
            string? errorCode = null;
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
                    errorCode = result.Errors.FirstOrDefault()?.Code
                        ?? HelloDeliveryErrorCodes.Failed;
                    AccountMessageProcessingFailed(
                        logger,
                        request.MessageId,
                        request.Kind,
                        errorCode);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                errorCode = HelloDeliveryErrorCodes.Failed;
                AccountMessageProcessingFailed(
                    logger,
                    request.MessageId,
                    request.Kind,
                    errorCode);
            }

            await TryFinishAsync(
                lease,
                errorCode,
                stoppingToken);
        }
    }

    private async Task TryFinishAsync(
        HelloAnonymousAccountMessageLease lease,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        var operation = errorCode is null ? "complete" : "fail";
        try
        {
            var result = errorCode is null
                ? await queue.CompleteAsync(lease, cancellationToken)
                : await queue.FailAsync(
                    lease,
                    errorCode,
                    cancellationToken);
            if (!result.IsSuccess)
            {
                AccountMessageQueueOperationFailed(
                    logger,
                    lease.Request.MessageId,
                    operation,
                    result.Errors.FirstOrDefault()?.Code
                        ?? HelloDeliveryErrorCodes.Failed,
                    null);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AccountMessageQueueOperationFailed(
                logger,
                lease.Request.MessageId,
                operation,
                HelloDeliveryErrorCodes.Failed,
                exception);
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

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Error,
        Message =
            "Anonymous account-message inbox operation failed. Message: {messageId}; operation: {operation}; error code: {errorCode}.")]
    private static partial void AccountMessageQueueOperationFailed(
        ILogger logger,
        Guid messageId,
        string operation,
        string errorCode,
        Exception? exception);
}
