using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

internal sealed class QueuedHelloAccountMessageSender(
    HelloAccountMessageQueue queue)
    : IHelloAccountMessageSender
{
    public Task<OperationResult> SendAsync(
        HelloAccountMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        var result = queue.TryWrite(message)
            ? OperationResultFactory.Success()
            : OperationResultFactory.Fail(
                new Error(
                    HelloDeliveryErrorCodes.QueueFull,
                    "The account message queue is full.",
                    ErrorType.Failure));
        return Task.FromResult(result);
    }
}
