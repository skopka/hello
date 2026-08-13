using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

public interface IHelloSecurityEventSink
{
    /// <summary>
    /// Receives a safe event after the Identity mutation has committed.
    /// Returning a failure or throwing cannot roll back that mutation.
    /// Implementations that trigger required follow-up work should durably
    /// enqueue it using <see cref="HelloSecurityEventEnvelope.EventId"/> as
    /// an idempotency key.
    /// </summary>
    OperationResult Write(HelloSecurityEventEnvelope securityEvent);
}
