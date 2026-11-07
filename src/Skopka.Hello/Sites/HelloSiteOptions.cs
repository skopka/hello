using Skopka.Identity.Authentication;

namespace Skopka.Hello;

/// <summary>Operations exposed by a configured public site, shared by Razor and HTTP transports.</summary>
[Flags]
public enum HelloSiteFeatures
{
    None = 0, Login = 1, Registration = 2, PasswordRecovery = 4,
    ContactConfirmation = 8, Account = 16, Sessions = 32,
    AccountSecurity = 64, ExternalIdentity = 128, All = 255,
}

/// <summary>Startup policy for one explicitly allowed public origin. Cookies remain host-only.</summary>
public sealed class HelloSiteOptions
{
    public required string Id { get; init; }
    public required Uri PublicOrigin { get; init; }
    public bool PasswordSignInEnabled { get; set; } = true;
    public bool PhoneOtpEnabled { get; set; } = true;
    public bool SelfRegistrationEnabled { get; set; } = true;
    public bool ExternalSignInEnabled { get; set; } = true;
    public bool WebAuthnSignInEnabled { get; set; } = true;
    public bool CrossDeviceSignInEnabled { get; set; } = true;
    public PasswordLoginHandle? PasswordLoginHandle { get; set; }
    public HelloSiteFeatures EnabledFeatures { get; set; } = HelloSiteFeatures.All;
    public string? ApplicationHomeUrl { get; set; }
    public string? PostLogoutRedirectPath { get; set; }
    public string? AuthenticatedRedirectPath { get; set; }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 128
            || Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.'))
            throw new InvalidOperationException("A site id must contain 1-128 ASCII letters, digits, dots, hyphens or underscores.");
        ValidateOrigin(PublicOrigin);
        if ((EnabledFeatures & ~HelloSiteFeatures.All) != 0
            || PasswordLoginHandle is { } handle && !Enum.IsDefined(handle))
            throw new InvalidOperationException("The site contains an unsupported feature or login handle.");
        foreach (var path in new[] { ApplicationHomeUrl, PostLogoutRedirectPath, AuthenticatedRedirectPath })
            if (path is not null && !IsLocalPath(path))
                throw new InvalidOperationException("Site destinations must be local absolute paths without an authority, escaping, query or fragment.");
    }

    internal static void ValidateOrigin(Uri? origin)
    {
        if (origin is null || !origin.IsAbsoluteUri || origin.Scheme != Uri.UriSchemeHttps
            || origin.UserInfo.Length != 0 || origin.AbsolutePath != "/"
            || origin.Query.Length != 0 || origin.Fragment.Length != 0)
            throw new InvalidOperationException("A site requires an HTTPS origin without credentials, path, query or fragment.");
    }

    private static bool IsLocalPath(string path) => path.StartsWith('/')
        && !path.StartsWith("//", StringComparison.Ordinal)
        && !path.Contains("//", StringComparison.Ordinal)
        && path.IndexOfAny(['\\', '?', '#', '%']) < 0
        && !path.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))
        && !path.Split('/').Any(part => part is "." or "..");
}

/// <summary>Immutable, validated public-site policy resolved from configuration, never from an arbitrary Host value.</summary>
public sealed record HelloSite(
    string Id, Uri? PublicOrigin, bool PasswordSignInEnabled, bool PhoneOtpEnabled,
    bool SelfRegistrationEnabled, bool ExternalSignInEnabled, bool WebAuthnSignInEnabled,
    bool CrossDeviceSignInEnabled, PasswordLoginHandle PasswordLoginHandle,
    HelloSiteFeatures EnabledFeatures, string? ApplicationHomeUrl,
    string? PostLogoutRedirectPath, string? AuthenticatedRedirectPath)
{
    public bool IsEnabled(HelloSiteFeatures feature) => (EnabledFeatures & feature) == feature;
}

/// <summary>Trusted names persisted with a session, independent of user-agent/display metadata.</summary>
public static class HelloAuthenticationMethods
{
    public const string Password = "password";
    public const string Phone = "phone";
    public const string External = "external";
    public const string Passkey = "passkey";
    public const string AccessLink = "access-link";
}
