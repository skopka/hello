namespace Skopka.Hello.Endpoints;

public sealed record TotpStateResponse(
    bool IsEnabled,
    int RecoveryCodesRemaining,
    DateTimeOffset? EnabledAt);

public sealed record TotpEnrollmentResponse(
    Guid EnrollmentId,
    string Secret,
    string ProvisioningUri,
    string QrCodeSvg,
    DateTimeOffset ExpiresAt);

public sealed record ConfirmTotpEnrollmentRequest(
    Guid EnrollmentId,
    string Code);

public sealed record ConfirmedTotpEnrollmentResponse(
    TotpStateResponse State,
    IReadOnlyList<string> RecoveryCodes);
