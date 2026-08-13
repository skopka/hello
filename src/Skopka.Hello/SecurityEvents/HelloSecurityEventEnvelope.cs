namespace Skopka.Hello;

public sealed record HelloSecurityEventEnvelope(
    Guid EventId,
    string EventType,
    Guid? SubjectUserId,
    Guid? ActorUserId,
    Guid? ResourceId,
    string? CorrelationId,
    DateTimeOffset OccurredAt,
    IReadOnlyDictionary<string, string> Metadata)
{
    /// <summary>
    /// Security events are observed after their Identity mutation commits.
    /// The value is part of the public contract so consumers do not infer a
    /// transactional guarantee from callback timing.
    /// </summary>
    public HelloSecurityEventDeliveryStage DeliveryStage { get; init; } =
        HelloSecurityEventDeliveryStage.AfterIdentityCommit;
}
