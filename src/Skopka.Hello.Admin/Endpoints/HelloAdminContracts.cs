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

public sealed record HelloAdminBeginRoleActionRequest(
    Guid? RoleId = null,
    Guid? TargetUserId = null,
    long? ExpectedVersion = null,
    string? Name = null,
    string? Description = null,
    Guid? ParentId = null);

public sealed record HelloAdminCompleteRoleActionRequest(
    Guid ChallengeId,
    string VerificationCode,
    Guid? RoleId = null,
    Guid? TargetUserId = null,
    long? ExpectedVersion = null,
    string? Name = null,
    string? Description = null,
    Guid? ParentId = null);
