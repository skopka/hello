using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

public interface IHelloAccountMessageSender
{
    Task<OperationResult> SendAsync(
        HelloAccountMessage message,
        CancellationToken cancellationToken);
}
