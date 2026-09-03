using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Sessions;
using Skopka.Identity.WebAuthn;

namespace Skopka.Hello.WebAuthn;

public static class HelloWebAuthnErrorCodes
{
    public const string Disabled = "hello.webauthn.disabled";

    public const string ChallengeInvalid = "hello.webauthn.challenge_invalid";

    public const string ChallengeSpent = "hello.webauthn.challenge_spent";

    public const string CredentialUnknown = "hello.webauthn.credential_unknown";

    public const string TooManyCredentials = "hello.webauthn.too_many_credentials";

    public const string LastSignInMethod = "hello.webauthn.last_sign_in_method";
}

/// <summary>
/// What a browser needs to call <c>navigator.credentials.create</c>, plus the
/// <paramref name="Ticket"/> it must send back. The ticket is the challenge
/// again, protected: the server keeps no row for a ceremony that may never be
/// finished, and cannot be talked into accepting a challenge it did not issue.
/// </summary>
public sealed record HelloWebAuthnRegistrationChallenge(
    string Ticket,
    string RelyingPartyId,
    string RelyingPartyName,
    byte[] Challenge,
    byte[] UserHandle,
    string UserName,
    string UserDisplayName,
    IReadOnlyList<byte[]> ExistingCredentialIds,
    IReadOnlyList<int> Algorithms,
    bool UserVerificationRequired,
    DateTimeOffset ExpiresAt);

/// <summary>
/// What a browser needs to call <c>navigator.credentials.get</c>. No credential
/// is named: a passkey identifies itself, which is what lets someone sign in
/// having typed nothing.
/// </summary>
public sealed record HelloWebAuthnAssertionChallenge(
    string Ticket,
    string RelyingPartyId,
    byte[] Challenge,
    bool UserVerificationRequired,
    DateTimeOffset ExpiresAt);

public sealed record HelloWebAuthnCredential(
    Guid Id,
    string? Label,
    bool BackedUp,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt);

public sealed record HelloBeginWebAuthnRegistrationCommand(
    string AccessToken,
    string? ClientKey = null);

public sealed record HelloCompleteWebAuthnRegistrationCommand(
    string AccessToken,
    string Ticket,
    byte[] ClientDataJson,
    byte[] AttestationObject,
    string? Label = null,
    string? ClientKey = null);

public sealed record HelloBeginWebAuthnSignInCommand(string? ClientKey = null);

public sealed record HelloCompleteWebAuthnSignInCommand(
    string Ticket,
    byte[] CredentialId,
    byte[] ClientDataJson,
    byte[] AuthenticatorData,
    byte[] Signature,
    IdentitySessionMetadata SessionMetadata,
    string? ClientKey = null);

public sealed record HelloRemoveWebAuthnCredentialCommand(
    string AccessToken,
    Guid CredentialId);

/// <summary>
/// Passkey registration and sign-in.
///
/// Registration needs a session, because a key is added to an account someone
/// is already holding. Signing in does not, because the assertion is the proof.
/// Both halves of both ceremonies go through here: the transport carries bytes
/// and never decides anything.
/// </summary>
public interface IHelloWebAuthnApplication<TProfile>
{
    Task<OperationResult<HelloWebAuthnRegistrationChallenge>>
        BeginRegistrationAsync(
            HelloBeginWebAuthnRegistrationCommand command,
            CancellationToken cancellationToken);

    Task<OperationResult<HelloWebAuthnCredential>> CompleteRegistrationAsync(
        HelloCompleteWebAuthnRegistrationCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloWebAuthnAssertionChallenge>> BeginSignInAsync(
        HelloBeginWebAuthnSignInCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloSignIn<TProfile>>> SignInAsync(
        HelloCompleteWebAuthnSignInCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<IReadOnlyList<HelloWebAuthnCredential>>> ListAsync(
        string accessToken,
        CancellationToken cancellationToken);

    Task<OperationResult> RemoveAsync(
        HelloRemoveWebAuthnCredentialCommand command,
        CancellationToken cancellationToken);
}

/// <summary>
/// The algorithms offered to an authenticator, in the order they are preferred.
/// Only what the verifier checks, because offering more would register keys
/// that cannot sign in.
/// </summary>
internal static class HelloWebAuthnAlgorithms
{
    public static readonly int[] Offered =
    [
        (int)WebAuthnAlgorithm.Es256,
        (int)WebAuthnAlgorithm.Rs256,
    ];
}
