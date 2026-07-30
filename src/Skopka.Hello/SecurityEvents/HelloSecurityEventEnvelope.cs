namespace Skopka.Hello;

public sealed record HelloSecurityEventEnvelope(
    Guid EventId,
    string EventType,
    Guid? SubjectUserId,
    Guid? ActorUserId,
    Guid? ResourceId,
    string? CorrelationId,
    DateTimeOffset OccurredAt,
    IReadOnlyDictionary<string, string> Metadata);
