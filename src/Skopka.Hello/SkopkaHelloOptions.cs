using Microsoft.AspNetCore.Http;
using Skopka.Identity.Authentication;

namespace Skopka.Hello;

public sealed class SkopkaHelloOptions
{
    public HelloTotpOptions Totp { get; } = new();

    public HelloWebAuthnOptions WebAuthn { get; } = new();

    public HelloRegistrationConsentOptions RegistrationConsent { get; } =
        new();

    public bool SelfRegistrationEnabled { get; set; } = true;

    public PasswordLoginHandle PasswordLoginHandle { get; set; } =
        Skopka.Identity.Authentication.PasswordLoginHandle.Automatic;

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
        WebAuthn.Validate(PublicOrigin);
        if (!Enum.IsDefined(PasswordLoginHandle))
        {
            throw new InvalidOperationException(
                "PasswordLoginHandle contains an unsupported value.");
        }

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

public sealed class HelloWebAuthnOptions
{
    public bool Enabled { get; set; }

    /// <summary>
    /// The domain a credential is bound to. Left empty it is taken from
    /// <see cref="SkopkaHelloOptions.PublicOrigin"/>, which is where the pages
    /// that will use it are served from anyway.
    /// </summary>
    public string RelyingPartyId { get; set; } = string.Empty;

    /// <summary>
    /// What an authenticator shows while asking. A name, not an address.
    /// </summary>
    public string RelyingPartyName { get; set; } = "Skopka.Hello";

    /// <summary>
    /// The addresses the ceremony may be answered from. Empty means the public
    /// origin alone, which is the only one a normal host serves.
    /// </summary>
    public IList<string> Origins { get; } = [];

    public bool UserVerificationRequired { get; set; } = true;

    /// <summary>
    /// Long enough for a person to find their key or their phone, short enough
    /// that an unanswered challenge stops being worth anything.
    /// </summary>
    public TimeSpan ChallengeLifetime { get; set; } = TimeSpan.FromMinutes(5);

    internal void Validate(Uri? publicOrigin)
    {
        if (!Enabled)
        {
            return;
        }

        // Both derived from the public origin when they were not given: a host
        // that already said where it lives should not have to say it twice, and
        // saying it twice differently is a ceremony that never completes.
        if (string.IsNullOrWhiteSpace(RelyingPartyId))
        {
            RelyingPartyId = publicOrigin?.Host
                ?? throw new InvalidOperationException(
                    "WebAuthn needs a relying party id, or a PublicOrigin to "
                    + "take one from.");
        }

        RelyingPartyId = RelyingPartyId.Trim();
        ArgumentException.ThrowIfNullOrWhiteSpace(RelyingPartyName);
        RelyingPartyName = RelyingPartyName.Trim();
        if (Origins.Count == 0)
        {
            Origins.Add(
                publicOrigin?.GetLeftPart(UriPartial.Authority)
                ?? throw new InvalidOperationException(
                    "WebAuthn needs an allowed origin, or a PublicOrigin to "
                    + "take one from."));
        }

        if (ChallengeLifetime <= TimeSpan.Zero
            || ChallengeLifetime > TimeSpan.FromMinutes(15))
        {
            throw new InvalidOperationException(
                "The WebAuthn challenge lifetime must be between nothing and "
                + "fifteen minutes.");
        }
    }
}
