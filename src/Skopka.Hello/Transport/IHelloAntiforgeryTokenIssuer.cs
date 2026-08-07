using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace Skopka.Hello;

public interface IHelloAntiforgeryTokenIssuer
{
    void Issue(
        HttpContext httpContext,
        DateTimeOffset? expiresAt = null);
}

internal sealed class HelloAntiforgeryTokenIssuer(
    IAntiforgery antiforgery,
    SkopkaHelloOptions options)
    : IHelloAntiforgeryTokenIssuer
{
    public void Issue(
        HttpContext httpContext,
        DateTimeOffset? expiresAt = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var tokens = antiforgery.GetAndStoreTokens(httpContext);
        if (String.IsNullOrWhiteSpace(tokens.RequestToken))
        {
            throw new InvalidOperationException(
                "The antiforgery service did not issue a request token.");
        }

        httpContext.Response.Cookies.Append(
            options.AntiforgeryRequestCookieName,
            tokens.RequestToken,
            new CookieOptions
            {
                HttpOnly = false,
                Secure = options.SecureCookies,
                IsEssential = true,
                SameSite = options.CookieSameSite,
                Path = "/",
                Expires = expiresAt,
            });
    }
}
