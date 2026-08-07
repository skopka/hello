using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

internal sealed class HelloSessionCookieManager(
    IAntiforgery antiforgery,
    IHelloAntiforgeryTokenIssuer antiforgeryTokens,
    SkopkaHelloOptions options)
    : IHelloSessionCookieManager
{
    public OperationResult ValidateTransport(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return options.SecureCookies && !httpContext.Request.IsHttps
            ? OperationResultFactory.Fail(
                new Error(
                    "hello.https.required",
                    "HTTPS is required for session cookies.",
                    ErrorType.Forbidden))
            : OperationResultFactory.Success();
    }

    public async Task<OperationResult> ValidateAntiforgeryAsync(
        HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
            return OperationResultFactory.Success();
        }
        catch (AntiforgeryValidationException)
        {
            return OperationResultFactory.Fail(
                new Error(
                    "hello.csrf.invalid",
                    "The CSRF token is missing or invalid.",
                    ErrorType.Forbidden));
        }
    }

    public string? ReadRefreshToken(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return httpContext.Request.Cookies.TryGetValue(
                options.RefreshCookieName,
                out var refreshToken)
            && !string.IsNullOrWhiteSpace(refreshToken)
                ? refreshToken
                : null;
    }

    public void WriteSessionCookies(
        HttpContext httpContext,
        HelloSession session)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(session);

        httpContext.Response.Cookies.Append(
            options.RefreshCookieName,
            session.RefreshToken,
            CreateCookieOptions(
                httpOnly: true,
                session.RefreshTokenExpiresAt));

        antiforgeryTokens.Issue(
            httpContext,
            session.RefreshTokenExpiresAt);
    }

    public void DeleteSessionCookies(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var cookie = CreateCookieOptions(
            httpOnly: true,
            expires: null);
        httpContext.Response.Cookies.Delete(
            options.RefreshCookieName,
            cookie);
        httpContext.Response.Cookies.Delete(
            options.AntiforgeryCookieName,
            cookie);
        httpContext.Response.Cookies.Delete(
            options.AntiforgeryRequestCookieName,
            CreateCookieOptions(
                httpOnly: false,
                expires: null));
    }

    private CookieOptions CreateCookieOptions(
        bool httpOnly,
        DateTimeOffset? expires)
        => new()
        {
            HttpOnly = httpOnly,
            Secure = options.SecureCookies,
            IsEssential = true,
            SameSite = options.CookieSameSite,
            Path = "/",
            Expires = expires,
        };
}
