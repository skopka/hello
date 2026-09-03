using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.UI;

/// <summary>
/// What a Razor page needs from a passkey, which is not what an API caller
/// needs. The page hands the browser a challenge as text and takes the
/// authenticator's answer back the same way, because a form posts strings; and
/// a sign-in here ends in the UI ticket rather than in a JSON session.
/// </summary>
public sealed record HelloUiWebAuthnChallenge(
    string Ticket,
    string RelyingPartyId,
    string RelyingPartyName,
    string Challenge,
    string UserHandle,
    string UserName,
    string UserDisplayName,
    IReadOnlyList<string> ExcludeCredentials,
    IReadOnlyList<int> Algorithms,
    bool UserVerificationRequired);

public sealed record HelloUiWebAuthnCredential(
    Guid Id,
    string? Label,
    bool BackedUp,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt);

public sealed record HelloUiWebAuthnAssertion(
    string Ticket,
    string CredentialId,
    string ClientDataJson,
    string AuthenticatorData,
    string Signature);

public sealed record HelloUiWebAuthnAttestation(
    string Ticket,
    string ClientDataJson,
    string AttestationObject,
    string? Label);

public interface IHelloUiWebAuthnApplication
{
    Task<OperationResult<HelloUiWebAuthnChallenge>> BeginSignInAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloUiSignIn>> SignInAsync(
        HelloUiWebAuthnAssertion assertion,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<OperationResult<IReadOnlyList<HelloUiWebAuthnCredential>>> ListAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloUiWebAuthnChallenge>> BeginRegistrationAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloUiWebAuthnCredential>> RegisterAsync(
        HelloUiWebAuthnAttestation attestation,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<OperationResult> RemoveAsync(
        Guid credentialId,
        HttpContext httpContext,
        CancellationToken cancellationToken);
}
