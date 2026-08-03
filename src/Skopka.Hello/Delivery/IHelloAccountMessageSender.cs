using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

public interface IHelloAccountMessageSender
{
    OperationResult CheckAvailability(HelloDeliveryChannel channel)
        => Enum.IsDefined(channel)
            ? OperationResultFactory.Success()
            : OperationResultFactory.Fail(
                new Error(
                    HelloDeliveryErrorCodes.NotConfigured,
                    "Account message delivery is not configured for this channel.",
                    ErrorType.Failure));

    Task<OperationResult> SendAsync(
        HelloAccountMessage message,
        CancellationToken cancellationToken);
}
