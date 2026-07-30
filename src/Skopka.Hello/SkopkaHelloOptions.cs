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
