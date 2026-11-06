using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Users;

namespace Skopka.Hello;

/// <summary>Host-owned live policy determining whether a user requires TOTP at sign-in.</summary>
public interface IHelloSignInSecondFactorPolicy<TProfile>
{
    Task<bool> RequiresAsync(IdentityUser<TProfile> user, CancellationToken cancellationToken);
}

/// <summary>A protected continuation, returned only after a primary factor succeeds.</summary>
public sealed class HelloSecondFactorRequired(string ticket, DateTimeOffset expiresAt, string? returnUrl = null)
{
    public string Ticket { get; } = ticket;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;
    /// <summary>Optional validated local continuation supplied by a browser flow.</summary>
    public string? ReturnUrl { get; } = returnUrl;
    public override string ToString() => "[protected second-factor continuation]";
}

public static class HelloSecondFactorErrorCodes
{
    public const string Required = "hello.sign_in.second_factor_required";
    public const string Invalid = "hello.sign_in.second_factor_invalid";
}

public enum HelloSecondFactorStage { Verify, Enroll, Acknowledge }
public sealed record HelloSecondFactorStatus(HelloSecondFactorStage Stage, DateTimeOffset ExpiresAt);
public sealed record HelloSecondFactorContinuation(string Ticket, DateTimeOffset ExpiresAt)
{
    public override string ToString() => "[protected second-factor continuation]";
}
public sealed record HelloSecondFactorEnrollment(string Ticket, string Secret, string ProvisioningUri, DateTimeOffset ExpiresAt)
{
    public override string ToString() => "[protected second-factor enrollment]";
}
public sealed record HelloSecondFactorRecovery(string Ticket, IReadOnlyList<string> RecoveryCodes, DateTimeOffset ExpiresAt)
{
    public override string ToString() => "[protected second-factor recovery codes]";
}

/// <summary>Completes a restricted primary-authentication continuation without an existing session.</summary>
public interface IHelloSecondFactorApplication<TProfile>
{
    Task<OperationResult<HelloSecondFactorStatus>> InspectAsync(string ticket, CancellationToken cancellationToken);
    Task<OperationResult<HelloSecondFactorContinuation>> BeginVerificationAsync(string ticket, string? clientKey, CancellationToken cancellationToken);
    Task<OperationResult<HelloSignIn<TProfile>>> CompleteVerificationAsync(string ticket, string code, string? clientKey, CancellationToken cancellationToken);
    Task<OperationResult<HelloSecondFactorEnrollment>> BeginEnrollmentAsync(string ticket, string? clientKey, CancellationToken cancellationToken);
    Task<OperationResult<HelloSecondFactorRecovery>> ConfirmEnrollmentAsync(string ticket, string code, string? clientKey, CancellationToken cancellationToken);
    Task<OperationResult<HelloSignIn<TProfile>>> AcknowledgeEnrollmentAsync(string ticket, CancellationToken cancellationToken);
}
