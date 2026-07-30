using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

public interface IHelloSecurityEventSink
{
    OperationResult Write(HelloSecurityEventEnvelope securityEvent);
}
