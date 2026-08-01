using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Sessions;

namespace Skopka.Hello.Oidc;

public enum HelloOidcCompletionKind
{
    SignedIn = 1,
    RegistrationRequired = 2,
    LinkPending = 3,
}

public sealed record HelloOidcRegistrationHints(
    HelloOidcProvider Provider,
    string? DisplayName,
    string? VerifiedEmail,
    string? Locale,
    string ReturnUrl);

public sealed record HelloOidcCompletion<TProfile>(
    HelloOidcCompletionKind Kind,
    HelloSignIn<TProfile>? SignIn,
    HelloOidcRegistrationHints? Registration,
    HelloOidcProvider? Provider,
    string ReturnUrl);

public sealed record HelloOidcLocalSession(
    Guid UserId,
    Guid SessionId,
    string AccessToken);

public sealed record HelloOidcRegisterCommand<TProfile>(
    string? UserName,
    string? Email,
    string? Phone,
    TProfile Profile,
    IdentitySessionMetadata SessionMetadata);

public sealed record HelloOidcLinkedProvider(
    string ProviderId,
    string DisplayName,
    bool Enabled,
    bool CanUnlink,
    DateTimeOffset LinkedAt);

public interface IHelloOidcApplication<TProfile>
{
    Task<OperationResult<HelloOidcCompletion<TProfile>>>
        CompleteChallengeAsync(
            HttpContext httpContext,
            HelloOidcLocalSession? localSession,
            IdentitySessionMetadata sessionMetadata,
            CancellationToken cancellationToken);

    Task<OperationResult<HelloOidcRegistrationHints>>
        GetRegistrationHintsAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken);

    Task<OperationResult<HelloSignIn<TProfile>>> RegisterAsync(
        HelloOidcRegisterCommand<TProfile> command,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<OperationResult<IReadOnlyList<HelloOidcLinkedProvider>>>
        ListLinkedProvidersAsync(
            string accessToken,
            CancellationToken cancellationToken);

    Task<OperationResult<HelloOidcProvider>> GetPendingLinkAsync(
        HttpContext httpContext,
        HelloOidcLocalSession localSession,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloStepUpChallenge>> BeginLinkAsync(
        HttpContext httpContext,
        HelloOidcLocalSession localSession,
        string? clientKey,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloSignIn<TProfile>>> CompleteLinkAsync(
        string verificationCode,
        HttpContext httpContext,
        HelloOidcLocalSession localSession,
        IdentitySessionMetadata sessionMetadata,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloStepUpChallenge>> BeginUnlinkAsync(
        string providerId,
        HttpContext httpContext,
        HelloOidcLocalSession localSession,
        string? clientKey,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloSignIn<TProfile>>> CompleteUnlinkAsync(
        string verificationCode,
        HttpContext httpContext,
        HelloOidcLocalSession localSession,
        IdentitySessionMetadata sessionMetadata,
        CancellationToken cancellationToken);

    Task ClearBrowserFlowAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken);
}
