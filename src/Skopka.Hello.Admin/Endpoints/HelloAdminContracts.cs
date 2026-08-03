namespace Skopka.Hello.Admin;

public sealed record HelloAdminBeginActionRequest(
    long? ExpectedVersion = null,
    DateTimeOffset? BlockedUntil = null,
    string? Reason = null);

public sealed record HelloAdminCompleteActionRequest(
    Guid ChallengeId,
    string VerificationCode,
    long? ExpectedVersion = null,
    DateTimeOffset? BlockedUntil = null,
    string? Reason = null);
