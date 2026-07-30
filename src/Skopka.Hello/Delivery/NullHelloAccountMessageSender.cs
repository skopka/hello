using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

internal sealed class NullHelloAccountMessageSender
    : IHelloAccountMessageSender
{
    public Task<OperationResult> SendAsync(
        HelloAccountMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            OperationResultFactory.Fail(
                new Error(
                    HelloDeliveryErrorCodes.NotConfigured,
                    "Account message delivery is not configured.",
                    ErrorType.Failure)));
    }
}
