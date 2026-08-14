using Microsoft.AspNetCore.Http;

namespace Skopka.Hello;

public sealed class SkopkaHelloOptions
{
    public HelloTotpOptions Totp { get; } = new();

    public bool SelfRegistrationEnabled { get; set; } = true;

    public int RegistrationClientPermitLimit { get; set; } = 5;

    public TimeSpan RegistrationClientWindow { get; set; } =
        TimeSpan.FromHours(1);

    public int RegistrationGlobalPermitLimit { get; set; } = 100;

    public TimeSpan RegistrationGlobalWindow { get; set; } =
        TimeSpan.FromMinutes(1);

    public string UiPathPrefix { get; set; } =
        HelloUiRoutePaths.DefaultPathPrefix;

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
        Totp.Validate();
        HelloUiRoutePaths.ValidatePathPrefix(UiPathPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(RefreshCookieName);
        ArgumentException.ThrowIfNullOrWhiteSpace(AntiforgeryCookieName);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            AntiforgeryRequestCookieName);
        ArgumentException.ThrowIfNullOrWhiteSpace(AntiforgeryHeaderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(ClientName);

        if (RegistrationClientPermitLimit <= 0
            || RegistrationClientWindow <= TimeSpan.Zero
            || RegistrationGlobalPermitLimit <= 0
            || RegistrationGlobalWindow <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Registration rate-limit permit counts and windows must be positive.");
        }

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

public sealed class HelloTotpOptions
{
    public bool Enabled { get; set; }

    public string Issuer { get; set; } = "Skopka.Hello";

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Issuer);
        Issuer = Issuer.Trim();
        if (Issuer.Length > 128
            || Issuer.Any(character => char.IsControl(character)))
        {
            throw new InvalidOperationException(
                "The TOTP issuer must contain at most 128 non-control characters.");
        }
    }
}
