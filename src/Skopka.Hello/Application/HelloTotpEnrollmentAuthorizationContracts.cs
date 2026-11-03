using Skopka.Abstraction.OperationResult;
using Skopka.Hello.WebAuthn;
using Skopka.Identity.ExternalLogins;

namespace Skopka.Hello;

public sealed record HelloTotpPhoneChallenge(string Ticket, DateTimeOffset ExpiresAt);

/// <summary>Fresh primary authentication for authenticator enrollment.</summary>
public interface IHelloTotpEnrollmentAuthorization<TProfile>
{
    Task<OperationResult<HelloTotpPhoneChallenge>> BeginPhoneAsync(
        string accessToken, string? clientKey, CancellationToken cancellationToken);

    Task<OperationResult<string>> CompletePhoneAsync(
        string accessToken, string ticket, string code, string? clientKey,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloWebAuthnAssertionChallenge>> BeginPasskeyAsync(
        string accessToken, string? clientKey, CancellationToken cancellationToken);

    Task<OperationResult<string>> CompletePasskeyAsync(
        string accessToken, string ticket, byte[] credentialId, byte[] clientDataJson,
        byte[] authenticatorData, byte[] signature, string? clientKey,
        CancellationToken cancellationToken);

    Task<OperationResult<string>> BeginExternalAsync(
        string accessToken, string provider, string? clientKey,
        CancellationToken cancellationToken);

    /// <summary>For trusted protocol adapters after fresh provider authentication.</summary>
    Task<OperationResult<string>> CompleteExternalAsync(
        string accessToken, string ticket, ExternalLoginKey login, string? clientKey,
        CancellationToken cancellationToken);
}
