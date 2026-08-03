using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

public interface IHelloAccountMessageProvider
{
    string ProviderId { get; }

    HelloDeliveryChannel Channel { get; }

    Task<OperationResult> SendAsync(
        HelloAccountMessage message,
        CancellationToken cancellationToken);
}
