using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

public sealed class NullHelloSecurityEventSink : IHelloSecurityEventSink
{
    public OperationResult Write(HelloSecurityEventEnvelope securityEvent)
    {
        ArgumentNullException.ThrowIfNull(securityEvent);
        return OperationResultFactory.Success();
    }
}
