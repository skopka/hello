namespace Skopka.Hello.Endpoints;

/// <summary>
/// Binary values travel base64url, which is the encoding WebAuthn itself uses
/// everywhere and the one a browser can hand to <c>navigator.credentials</c>
/// after a single decode.
/// </summary>
public sealed record WebAuthnRegistrationChallengeResponse(
    string Ticket,
    string RelyingPartyId,
    string RelyingPartyName,
    string Challenge,
    string UserHandle,
    string UserName,
    string UserDisplayName,
    IReadOnlyList<string> ExcludeCredentials,
    IReadOnlyList<int> Algorithms,
    bool UserVerificationRequired,
    DateTimeOffset ExpiresAt);

public sealed record WebAuthnAssertionChallengeResponse(
    string Ticket,
    string RelyingPartyId,
    string Challenge,
    bool UserVerificationRequired,
    DateTimeOffset ExpiresAt);

public sealed record CompleteWebAuthnRegistrationRequest(
    string Ticket,
    string ClientDataJson,
    string AttestationObject,
    string? Label);

public sealed record CompleteWebAuthnSignInRequest(
    string Ticket,
    string CredentialId,
    string ClientDataJson,
    string AuthenticatorData,
    string Signature);

/// <summary>
/// What a person needs to recognise one of their own keys. No public key, no
/// credential identifier and no authenticator model: none of it helps them
/// choose, and all of it is worth something to somebody else.
/// </summary>
public sealed record WebAuthnCredentialResponse(
    Guid Id,
    string? Label,
    bool BackedUp,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt);
