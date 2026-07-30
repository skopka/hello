using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace Skopka.Hello.UI;

internal sealed class HelloUiCookieAuthenticationEvents<TProfile>(
    IHelloIdentityApplication<TProfile> application,
    IHelloSessionCookieManager sessionCookies,
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
            context.ReplacePrincipal(
                HelloUiPrincipalFactory.Create(
                    validated.Value,
                    sessionId,
                    profiles));
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
        context.ReplacePrincipal(
            HelloUiPrincipalFactory.Create(
                account.Value,
                refreshed.Value.SessionId,
                profiles));
        context.ShouldRenew = true;
    }

    private void Reject(CookieValidatePrincipalContext context)
    {
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
