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
    DateTimeOffset ExpiresAt,
    string EnrollmentTicket);

public sealed record BeginTotpEnrollmentRequest(string? Password, string? Proof);
public sealed record CompleteTotpPhoneProofRequest(string Ticket, string Code);
public sealed record TotpProofResponse(string Proof);

public sealed record ConfirmTotpEnrollmentRequest(
    Guid EnrollmentId,
    string Code,
    string? EnrollmentTicket = null);

public sealed record ConfirmedTotpEnrollmentResponse(
    TotpStateResponse State,
    IReadOnlyList<string> RecoveryCodes);
