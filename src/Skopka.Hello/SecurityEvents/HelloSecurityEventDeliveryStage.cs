namespace Skopka.Hello;

/// <summary>
/// Describes when a Hello security event is delivered relative to the
/// Identity mutation that produced it.
/// </summary>
public enum HelloSecurityEventDeliveryStage
{
    /// <summary>
    /// The Identity mutation has already committed. A sink failure cannot
    /// roll it back, so durable follow-up work must be enqueued idempotently.
    /// </summary>
    AfterIdentityCommit = 0,
}
