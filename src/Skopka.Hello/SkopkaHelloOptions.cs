using Microsoft.AspNetCore.Http;

namespace Skopka.Hello;

public sealed class SkopkaHelloOptions
{
    public string RefreshCookieName { get; set; } =
        "__Host-Skopka.Hello.Refresh";

    public string AntiforgeryCookieName { get; set; } =
        "__Host-Skopka.Hello.Antiforgery";

    public string AntiforgeryRequestCookieName { get; set; } =
        "__Host-Skopka.Hello.XSRF-TOKEN";

    public string AntiforgeryHeaderName { get; set; } =
        "X-CSRF-TOKEN";

    public string ClientName { get; set; } = "Skopka.Hello";

    public Uri? PublicOrigin { get; set; }

    public bool SecureCookies { get; set; } = true;

    public SameSiteMode CookieSameSite { get; set; } =
        SameSiteMode.Strict;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RefreshCookieName);
        ArgumentException.ThrowIfNullOrWhiteSpace(AntiforgeryCookieName);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            AntiforgeryRequestCookieName);
        ArgumentException.ThrowIfNullOrWhiteSpace(AntiforgeryHeaderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(ClientName);

        if (PublicOrigin is not null
            && (!PublicOrigin.IsAbsoluteUri
                || (PublicOrigin.Scheme != Uri.UriSchemeHttps
                    && PublicOrigin.Scheme != Uri.UriSchemeHttp)
                || PublicOrigin.UserInfo.Length > 0
                || PublicOrigin.Query.Length > 0
                || PublicOrigin.Fragment.Length > 0
                || PublicOrigin.AbsolutePath != "/"))
        {
            throw new InvalidOperationException(
                "PublicOrigin must be an absolute HTTP(S) origin without credentials, path, query or fragment.");
        }

        if (!SecureCookies
            && new[]
                {
                    RefreshCookieName,
                    AntiforgeryCookieName,
                    AntiforgeryRequestCookieName,
                }
                .Any(name => name.StartsWith(
                    "__Host-",
                    StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "__Host- cookies must always be Secure.");
        }
    }
}
