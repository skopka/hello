using Microsoft.AspNetCore.Http;

namespace Skopka.Hello;

public interface IHelloCrossDeviceCookieManager
{
    void Write(
        HttpContext httpContext,
        string deviceCode,
        string browserVerifier,
        DateTimeOffset expiresAt);

    bool TryRead(
        HttpContext httpContext,
        string deviceCode,
        out string browserVerifier);

    void Delete(HttpContext httpContext);
}

internal sealed class HelloCrossDeviceCookieManager(
    HelloCrossDeviceSignInOptions options)
    : IHelloCrossDeviceCookieManager
{
    private const char Separator = '~';

    public void Write(
        HttpContext httpContext,
        string deviceCode,
        string browserVerifier,
        DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(browserVerifier);

        httpContext.Response.Cookies.Append(
            options.VerifierCookieName,
            $"{deviceCode}{Separator}{browserVerifier}",
            CreateCookieOptions(expiresAt));
    }

    public bool TryRead(
        HttpContext httpContext,
        string deviceCode,
        out string browserVerifier)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        browserVerifier = string.Empty;
        if (!httpContext.Request.Cookies.TryGetValue(
                options.VerifierCookieName,
                out var value))
        {
            return false;
        }

        var separator = value.IndexOf(Separator);
        if (separator <= 0
            || !string.Equals(
                value[..separator],
                deviceCode,
                StringComparison.Ordinal)
            || separator == value.Length - 1)
        {
            return false;
        }

        browserVerifier = value[(separator + 1)..];
        return true;
    }

    public void Delete(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        httpContext.Response.Cookies.Delete(
            options.VerifierCookieName,
            CreateCookieOptions(expiresAt: null));
    }

    private CookieOptions CreateCookieOptions(DateTimeOffset? expiresAt)
        => new()
        {
            HttpOnly = true,
            Secure = true,
            IsEssential = true,
            SameSite = options.CookieSameSite,
            Path = "/",
            Expires = expiresAt,
        };
}
