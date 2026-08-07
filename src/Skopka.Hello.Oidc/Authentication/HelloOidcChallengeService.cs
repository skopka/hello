using Microsoft.AspNetCore.Authentication;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.Oidc;

public sealed record HelloOidcChallenge(
    string AuthenticationScheme,
    AuthenticationProperties Properties);

public interface IHelloOidcChallengeService
{
    OperationResult<HelloOidcChallenge> CreateSignIn(
        string providerId,
        string? returnUrl);

    OperationResult<HelloOidcChallenge> CreateLink(
        string providerId,
        Guid userId,
        Guid sessionId);

    OperationResult<HelloOidcChallenge> CreateHeadlessSignIn(
        string providerId,
        string returnUrl);

    OperationResult<HelloOidcChallenge> CreateHeadlessLink(
        string providerId,
        string returnUrl,
        Guid userId,
        Guid sessionId);
}

internal sealed class HelloOidcChallengeService(
    HelloOidcProviderCatalog providers,
    HelloOidcOptions options,
    HelloUiRoutePaths uiRoutes)
    : IHelloOidcChallengeService
{
    public OperationResult<HelloOidcChallenge> CreateSignIn(
        string providerId,
        string? returnUrl)
        => Create(
            providerId,
            HelloOidcProperties.SignInIntent,
            HelloOidcReturnUrl.Normalize(
                returnUrl,
                uiRoutes.AccountPath,
                uiRoutes.ExternalCompletionPath),
            uiRoutes.ExternalCompletionPath,
            userId: null,
            sessionId: null,
            headless: false);

    public OperationResult<HelloOidcChallenge> CreateHeadlessSignIn(
        string providerId,
        string returnUrl)
    {
        if (!HelloOidcReturnUrl.TryNormalizeHeadless(
                returnUrl,
                out var normalizedReturnUrl))
        {
            return OperationResultFactory.Fail<HelloOidcChallenge>(
                HelloOidcErrors.ReturnUrlInvalid());
        }

        return Create(
            providerId,
            HelloOidcProperties.SignInIntent,
            normalizedReturnUrl,
            normalizedReturnUrl,
            userId: null,
            sessionId: null,
            headless: true);
    }

    public OperationResult<HelloOidcChallenge> CreateLink(
        string providerId,
        Guid userId,
        Guid sessionId)
    {
        if (userId == Guid.Empty || sessionId == Guid.Empty)
        {
            return OperationResultFactory.Fail<HelloOidcChallenge>(
                HelloOidcErrors.PendingIdentityInvalid());
        }

        return Create(
            providerId,
            HelloOidcProperties.LinkIntent,
            uiRoutes.ExternalLoginsPath,
            uiRoutes.ExternalCompletionPath,
            userId,
            sessionId,
            headless: false);
    }

    public OperationResult<HelloOidcChallenge> CreateHeadlessLink(
        string providerId,
        string returnUrl,
        Guid userId,
        Guid sessionId)
    {
        if (userId == Guid.Empty
            || sessionId == Guid.Empty)
        {
            return OperationResultFactory.Fail<HelloOidcChallenge>(
                HelloOidcErrors.PendingIdentityInvalid());
        }

        if (!HelloOidcReturnUrl.TryNormalizeHeadless(
                returnUrl,
                out var normalizedReturnUrl))
        {
            return OperationResultFactory.Fail<HelloOidcChallenge>(
                HelloOidcErrors.ReturnUrlInvalid());
        }

        return Create(
            providerId,
            HelloOidcProperties.LinkIntent,
            normalizedReturnUrl,
            normalizedReturnUrl,
            userId,
            sessionId,
            headless: true);
    }

    private OperationResult<HelloOidcChallenge> Create(
        string providerId,
        string intent,
        string returnUrl,
        string redirectUri,
        Guid? userId,
        Guid? sessionId,
        bool headless)
    {
        if (!providers.TryGet(providerId, out var provider))
        {
            return OperationResultFactory.Fail<HelloOidcChallenge>(
                HelloOidcErrors.ProviderUnavailable());
        }

        var now = DateTimeOffset.UtcNow;
        var properties = new AuthenticationProperties
        {
            AllowRefresh = false,
            IsPersistent = false,
            IssuedUtc = now,
            ExpiresUtc = now.Add(options.ExternalCookieLifetime),
            RedirectUri = redirectUri,
        };
        properties.Items[HelloOidcProperties.Intent] = intent;
        properties.Items[HelloOidcProperties.Provider] = provider.Id;
        properties.Items[HelloOidcProperties.ReturnUrl] = returnUrl;
        properties.Items[HelloOidcProperties.FlowId] =
            HelloOidcFlowId.Create().ToString("D");
        if (headless)
        {
            properties.Items[HelloOidcProperties.Headless] =
                Boolean.TrueString;
        }
        if (userId is not null && sessionId is not null)
        {
            properties.Items[HelloOidcProperties.UserId] =
                userId.Value.ToString("D");
            properties.Items[HelloOidcProperties.SessionId] =
                sessionId.Value.ToString("D");
        }

        return OperationResultFactory.Success(
            new HelloOidcChallenge(
                provider.AuthenticationScheme,
                properties));
    }
}
