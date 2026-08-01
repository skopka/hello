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
}

internal sealed class HelloOidcChallengeService(
    HelloOidcProviderCatalog providers,
    HelloOidcOptions options)
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
                "/hello/account"),
            userId: null,
            sessionId: null);

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
            HelloOidcDefaults.ExternalLoginsPath,
            userId,
            sessionId);
    }

    private OperationResult<HelloOidcChallenge> Create(
        string providerId,
        string intent,
        string returnUrl,
        Guid? userId,
        Guid? sessionId)
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
            RedirectUri = HelloOidcDefaults.CompletionPath,
        };
        properties.Items[HelloOidcProperties.Intent] = intent;
        properties.Items[HelloOidcProperties.Provider] = provider.Id;
        properties.Items[HelloOidcProperties.ReturnUrl] = returnUrl;
        properties.Items[HelloOidcProperties.FlowId] =
            HelloOidcFlowId.Create().ToString("D");
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
