namespace Skopka.Hello;

public sealed record HelloAuditOutboxRecord(
    Guid Id,
    string EventType,
    Guid? SubjectUserId,
    Guid? ActorUserId,
    Guid? ResourceId,
    string? CorrelationId,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, string> Metadata);
