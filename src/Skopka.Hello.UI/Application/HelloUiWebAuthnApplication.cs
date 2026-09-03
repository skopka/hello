using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.WebAuthn;
using Skopka.Identity.Errors;
using Base64Url = Microsoft.AspNetCore.WebUtilities.Base64UrlTextEncoder;

namespace Skopka.Hello.UI;

internal sealed class HelloUiWebAuthnApplication<TProfile>(
    IHelloWebAuthnApplication<TProfile> application,
    IHelloRequestContext requestContext,
    IHelloUiProfileFactory<TProfile> profiles,
    SkopkaHelloOptions helloOptions)
    : IHelloUiWebAuthnApplication
{
    public async Task<OperationResult<HelloUiWebAuthnChallenge>> BeginSignInAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var issued = await application.BeginSignInAsync(
            new HelloBeginWebAuthnSignInCommand(
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
        return issued.IsSuccess
            ? OperationResultFactory.Success(new HelloUiWebAuthnChallenge(
                issued.Value.Ticket,
                issued.Value.RelyingPartyId,
                RelyingPartyName: string.Empty,
                Base64Url.Encode(issued.Value.Challenge),
                UserHandle: string.Empty,
                UserName: string.Empty,
                UserDisplayName: string.Empty,
                ExcludeCredentials: [],
                Algorithms: [],
                issued.Value.UserVerificationRequired))
            : OperationResultFactory.Fail<HelloUiWebAuthnChallenge>(
                issued.Errors);
    }

    public async Task<OperationResult<HelloUiSignIn>> SignInAsync(
        HelloUiWebAuthnAssertion assertion,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        ArgumentNullException.ThrowIfNull(httpContext);
        if (!TryDecode(assertion.CredentialId, out var credentialId)
            || !TryDecode(assertion.ClientDataJson, out var clientData)
            || !TryDecode(assertion.AuthenticatorData, out var authenticatorData)
            || !TryDecode(assertion.Signature, out var signature))
        {
            return Unreadable<HelloUiSignIn>();
        }

        var signedIn = await application.SignInAsync(
            new HelloCompleteWebAuthnSignInCommand(
                assertion.Ticket,
                credentialId,
                clientData,
                authenticatorData,
                signature,
                requestContext.CreateSessionMetadata(
                    httpContext,
                    helloOptions.ClientName),
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
        return signedIn.IsSuccess
            ? OperationResultFactory.Success(new HelloUiSignIn(
                HelloUiPrincipalFactory.Create(
                    signedIn.Value.Account,
                    signedIn.Value.Session.SessionId,
                    profiles),
                signedIn.Value.Session))
            : OperationResultFactory.Fail<HelloUiSignIn>(signedIn.Errors);
    }

    public async Task<OperationResult<IReadOnlyList<HelloUiWebAuthnCredential>>>
        ListAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (await ReadAccessTokenAsync(httpContext) is not { } accessToken)
        {
            return InvalidSession<IReadOnlyList<HelloUiWebAuthnCredential>>();
        }

        var listed = await application.ListAsync(accessToken, cancellationToken);
        return listed.IsSuccess
            ? OperationResultFactory.Success<
                IReadOnlyList<HelloUiWebAuthnCredential>>(
                    [.. listed.Value.Select(Describe)])
            : OperationResultFactory.Fail<
                IReadOnlyList<HelloUiWebAuthnCredential>>(listed.Errors);
    }

    public async Task<OperationResult<HelloUiWebAuthnChallenge>>
        BeginRegistrationAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (await ReadAccessTokenAsync(httpContext) is not { } accessToken)
        {
            return InvalidSession<HelloUiWebAuthnChallenge>();
        }

        var issued = await application.BeginRegistrationAsync(
            new HelloBeginWebAuthnRegistrationCommand(
                accessToken,
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
        return issued.IsSuccess
            ? OperationResultFactory.Success(new HelloUiWebAuthnChallenge(
                issued.Value.Ticket,
                issued.Value.RelyingPartyId,
                issued.Value.RelyingPartyName,
                Base64Url.Encode(issued.Value.Challenge),
                Base64Url.Encode(issued.Value.UserHandle),
                issued.Value.UserName,
                issued.Value.UserDisplayName,
                [.. issued.Value.ExistingCredentialIds.Select(
                    Base64Url.Encode)],
                issued.Value.Algorithms,
                issued.Value.UserVerificationRequired))
            : OperationResultFactory.Fail<HelloUiWebAuthnChallenge>(
                issued.Errors);
    }

    public async Task<OperationResult<HelloUiWebAuthnCredential>> RegisterAsync(
        HelloUiWebAuthnAttestation attestation,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attestation);
        ArgumentNullException.ThrowIfNull(httpContext);
        if (await ReadAccessTokenAsync(httpContext) is not { } accessToken)
        {
            return InvalidSession<HelloUiWebAuthnCredential>();
        }

        if (!TryDecode(attestation.ClientDataJson, out var clientData)
            || !TryDecode(attestation.AttestationObject, out var attestationObject))
        {
            return Unreadable<HelloUiWebAuthnCredential>();
        }

        var registered = await application.CompleteRegistrationAsync(
            new HelloCompleteWebAuthnRegistrationCommand(
                accessToken,
                attestation.Ticket,
                clientData,
                attestationObject,
                attestation.Label,
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
        return registered.IsSuccess
            ? OperationResultFactory.Success(Describe(registered.Value))
            : OperationResultFactory.Fail<HelloUiWebAuthnCredential>(
                registered.Errors);
    }

    public async Task<OperationResult> RemoveAsync(
        Guid credentialId,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return await ReadAccessTokenAsync(httpContext) is { } accessToken
            ? await application.RemoveAsync(
                new HelloRemoveWebAuthnCredentialCommand(
                    accessToken,
                    credentialId),
                cancellationToken)
            : InvalidSession();
    }

    private static HelloUiWebAuthnCredential Describe(
        HelloWebAuthnCredential credential)
        => new(
            credential.Id,
            credential.Label,
            credential.BackedUp,
            credential.CreatedAt,
            credential.LastUsedAt);

    /// <summary>
    /// A field the browser filled in is base64url or it is nothing. Answered
    /// here rather than passed on, so that a form somebody edited by hand does
    /// not reach the verifier at all.
    /// </summary>
    private static bool TryDecode(string? value, out byte[] decoded)
    {
        decoded = [];
        if (string.IsNullOrEmpty(value) || value.Length > 32768)
        {
            return false;
        }

        try
        {
            decoded = Base64Url.Decode(value);
            return decoded.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static async Task<string?> ReadAccessTokenAsync(
        HttpContext httpContext)
    {
        var authentication = await httpContext.AuthenticateAsync(
            HelloUiDefaults.AuthenticationScheme);
        return authentication.Succeeded
            ? authentication.Properties?.GetTokenValue(
                HelloUiDefaults.AccessTokenName)
            : null;
    }

    private static OperationResult<T> Unreadable<T>()
        => OperationResultFactory.Fail<T>(new Error(
            HelloWebAuthnErrorCodes.ChallengeInvalid,
            "The authenticator response cannot be read.",
            ErrorType.Validation));

    private static OperationResult InvalidSession()
        => OperationResultFactory.Fail(new Error(
            IdentityErrorCodes.RefreshTokenInvalid,
            "The session is invalid or expired.",
            ErrorType.Unauthorized));

    private static OperationResult<T> InvalidSession<T>()
        => OperationResultFactory.Fail<T>(new Error(
            IdentityErrorCodes.RefreshTokenInvalid,
            "The session is invalid or expired.",
            ErrorType.Unauthorized));
}
