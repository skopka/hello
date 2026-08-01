using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.Oidc;

namespace Skopka.Hello.UI;

public sealed record HelloUiExternalCompletion(
    HelloOidcCompletionKind Kind,
    HelloUiSignIn? SignIn,
    HelloOidcRegistrationHints? Registration,
    HelloOidcProvider? Provider,
    string ReturnUrl);

public sealed record HelloUiExternalRegisterCommand(
    string? UserName,
    string? Email,
    string? Phone,
    string DisplayName,
    string? Locale);

public sealed record HelloUiExternalRegistration(
    HelloUiSignIn SignIn,
    string ReturnUrl);

public interface IHelloUiExternalApplication
{
    bool IsConfigured { get; }

    IReadOnlyList<HelloOidcProvider> Providers { get; }

    OperationResult<HelloOidcChallenge> CreateSignInChallenge(
        string providerId,
        string? returnUrl);

    OperationResult<HelloOidcChallenge> CreateLinkChallenge(
        string providerId,
        HttpContext httpContext);

    Task<OperationResult<HelloUiExternalCompletion>>
        CompleteChallengeAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken);

    Task<OperationResult<HelloOidcRegistrationHints>>
        GetRegistrationHintsAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken);

    Task<OperationResult<HelloUiExternalRegistration>> RegisterAsync(
        HelloUiExternalRegisterCommand command,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<OperationResult<IReadOnlyList<HelloOidcLinkedProvider>>>
        ListLinkedProvidersAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken);

    Task<OperationResult<HelloOidcProvider>> GetPendingLinkAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloStepUpChallenge>> BeginLinkAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloUiSignIn>> CompleteLinkAsync(
        string verificationCode,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloStepUpChallenge>> BeginUnlinkAsync(
        string providerId,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloUiSignIn>> CompleteUnlinkAsync(
        string verificationCode,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task ClearBrowserFlowAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken);
}
