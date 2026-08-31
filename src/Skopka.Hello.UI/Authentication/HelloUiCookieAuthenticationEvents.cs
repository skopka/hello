using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace Skopka.Hello.UI;

internal sealed class HelloUiCookieAuthenticationEvents<TProfile>(
    IHelloIdentityApplication<TProfile> application,
    IHelloSessionCookieManager sessionCookies,
    IHelloUiAccountSwitcher accountSwitcher,
    IHelloUiProfileFactory<TProfile> profiles,
    SkopkaHelloUiOptions options)
    : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(
        CookieValidatePrincipalContext context)
    {
        var accessToken = context.Properties.GetTokenValue(
            HelloUiDefaults.AccessTokenName);
        if (context.Principal is null
            || string.IsNullOrWhiteSpace(accessToken)
            || !HelloUiPrincipalFactory.TryGetSessionId(
                context.Principal,
                out var sessionId))
        {
            Reject(context);
            return;
        }

        var validated = await application.ValidateAccessTokenAsync(
            accessToken,
            context.HttpContext.RequestAborted);
        if (validated.IsSuccess)
        {
            var validatedPrincipal = HelloUiPrincipalFactory.Create(
                    validated.Value,
                    sessionId,
                    profiles);
            context.ReplacePrincipal(validatedPrincipal);
            var currentRefreshToken = sessionCookies.ReadRefreshToken(
                context.HttpContext);
            if (currentRefreshToken is not null
                && context.Properties.ExpiresUtc is { } expiresAt)
            {
                accountSwitcher.Save(
                    context.HttpContext,
                    validatedPrincipal,
                    new HelloSession(
                        sessionId,
                        accessToken,
                        expiresAt,
                        currentRefreshToken,
                        expiresAt));
            }

            return;
        }

        var refreshToken = sessionCookies.ReadRefreshToken(
            context.HttpContext);
        if (refreshToken is null)
        {
            Reject(context);
            return;
        }

        var refreshed = await application.RefreshAsync(
            refreshToken,
            context.HttpContext.RequestAborted);
        if (!refreshed.IsSuccess)
        {
            Reject(context);
            return;
        }

        var account = await application.ValidateAccessTokenAsync(
            refreshed.Value.AccessToken,
            context.HttpContext.RequestAborted);
        if (!account.IsSuccess)
        {
            Reject(context);
            return;
        }

        sessionCookies.WriteSessionCookies(
            context.HttpContext,
            refreshed.Value);
        context.Properties.StoreTokens(
        [
            new AuthenticationToken
            {
                Name = HelloUiDefaults.AccessTokenName,
                Value = refreshed.Value.AccessToken,
            },
        ]);
        context.Properties.ExpiresUtc =
            refreshed.Value.RefreshTokenExpiresAt;
        var refreshedPrincipal = HelloUiPrincipalFactory.Create(
                account.Value,
                refreshed.Value.SessionId,
                profiles);
        context.ReplacePrincipal(refreshedPrincipal);
        accountSwitcher.Save(
            context.HttpContext,
            refreshedPrincipal,
            refreshed.Value);
        context.ShouldRenew = true;
    }

    private void Reject(CookieValidatePrincipalContext context)
    {
        if (context.Principal is not null
            && HelloUiPrincipalFactory.TryGetSessionId(
                context.Principal,
                out var sessionId))
        {
            accountSwitcher.RemoveSession(
                context.HttpContext,
                sessionId);
        }

        context.RejectPrincipal();
        sessionCookies.DeleteSessionCookies(context.HttpContext);
        context.HttpContext.Response.Cookies.Delete(
            options.AuthenticationCookieName,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = options.SecureCookies,
                IsEssential = true,
                SameSite = options.CookieSameSite,
                Path = "/",
            });
    }
}
