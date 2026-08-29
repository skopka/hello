namespace Skopka.Hello.Endpoints;

public sealed record BeginCrossDeviceSignInRequest(
    string? ReturnUrl = null,
    string? ClientId = null);

public sealed record BeginCrossDeviceSignInResponse(
    Guid RequestId,
    string DeviceCode,
    string UserCode,
    string ApprovalUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record CrossDeviceSignInStatusResponse(
    string State,
    string UserCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ResolvedAt);

public sealed record CrossDeviceApprovalDetailsResponse(
    string DeviceCode,
    string UserCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string? IpAddress,
    string? UserAgent,
    string? DeviceDisplayName);

public sealed record ApproveCrossDeviceSignInRequest(
    Guid ChallengeId,
    string TotpCode);

public sealed record CompleteCrossDeviceSignInResponse(
    SessionResponse Session,
    string? ClientId,
    string? ReturnUrl);
