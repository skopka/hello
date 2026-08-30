using Microsoft.AspNetCore.Http;

namespace Skopka.Hello.UI;

public sealed class SkopkaHelloUiOptions
{
    public const string DefaultCustomCssRequestPath =
        "/_content/Skopka.Hello.UI/custom.css";

    public string? CustomCssFilePath { get; set; }

    public string CustomCssRequestPath { get; set; } =
        DefaultCustomCssRequestPath;

    public string AuthenticationCookieName { get; set; } =
        "__Host-Skopka.Hello.UI";

    public bool SecureCookies { get; set; } = true;

    public SameSiteMode CookieSameSite { get; set; } =
        SameSiteMode.Strict;

    public bool BuiltInStylesEnabled { get; set; } = true;

    public string? LayoutPath { get; set; }

    public HelloUiPages EnabledPages { get; set; } =
        HelloUiPages.All;

    public string? AuthenticatedRedirectPath { get; set; }

    public string? ApplicationHomeUrl { get; set; }

    public string? NoticeText { get; set; }

    public string? TermsOfServiceUrl { get; set; }

    public string? PrivacyPolicyUrl { get; set; }

    public HelloUiRegistrationOptions Registration { get; } = new();

    public HelloUiAccountOptions Account { get; } = new();

    public HelloUiContactConfirmationOptions ContactConfirmation { get; } =
        new();

    public SkopkaHelloUiLocalizationOptions Localization { get; } =
        new();

    public void Validate()
    {
        Registration.Validate();
        Account.Validate();
        ContactConfirmation.Validate();
        Localization.Validate();

        if ((EnabledPages & ~HelloUiPages.All) != 0)
        {
            throw new InvalidOperationException(
                "EnabledPages contains an unsupported page flag.");
        }

        var pagesRequiringLogin = EnabledPages & ~HelloUiPages.Login;
        if (pagesRequiringLogin != HelloUiPages.None
            && !IsEnabled(HelloUiPages.Login))
        {
            throw new InvalidOperationException(
                "Registration, recovery, confirmation and account pages require the Login page.");
        }

        var pagesRequiringAccount = EnabledPages
            & (HelloUiPages.Sessions
                | HelloUiPages.AccountSecurity
                | HelloUiPages.ExternalIdentity);
        if (pagesRequiringAccount != HelloUiPages.None
            && !IsEnabled(HelloUiPages.Account))
        {
            throw new InvalidOperationException(
                "Sessions, AccountSecurity and ExternalIdentity pages require the Account page.");
        }

        if (IsEnabled(HelloUiPages.Login)
            && !IsEnabled(HelloUiPages.Account)
            && AuthenticatedRedirectPath is null)
        {
            throw new InvalidOperationException(
                "AuthenticatedRedirectPath is required when Login is enabled and Account is disabled.");
        }

        if (AuthenticatedRedirectPath is not null
            && !IsLocalAbsolutePath(AuthenticatedRedirectPath))
        {
            throw new InvalidOperationException(
                "AuthenticatedRedirectPath must be a local absolute path without an authority, query, fragment, escaping, whitespace or dot segments.");
        }

        if (ApplicationHomeUrl is not null
            && !IsSafeApplicationHomeUrl(ApplicationHomeUrl))
        {
            throw new InvalidOperationException(
                "ApplicationHomeUrl must be a local absolute path or an absolute HTTPS URL without credentials, a query, a fragment or unsafe path segments.");
        }

        ValidateLegalDocumentUrl(
            TermsOfServiceUrl,
            nameof(TermsOfServiceUrl));
        ValidateLegalDocumentUrl(
            PrivacyPolicyUrl,
            nameof(PrivacyPolicyUrl));
        ValidateLayoutPath(LayoutPath);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            CustomCssRequestPath);

        if (CustomCssRequestPath.Length > 256
            || CustomCssRequestPath == "/"
            || !CustomCssRequestPath.StartsWith('/')
            || CustomCssRequestPath.EndsWith('/')
            || CustomCssRequestPath.Contains("//", StringComparison.Ordinal)
            || CustomCssRequestPath.Contains("/./", StringComparison.Ordinal)
            || CustomCssRequestPath.Contains("/../", StringComparison.Ordinal)
            || CustomCssRequestPath.EndsWith("/.", StringComparison.Ordinal)
            || CustomCssRequestPath.EndsWith("/..", StringComparison.Ordinal)
            || CustomCssRequestPath.IndexOfAny(
                ['?', '#', '\\', '{', '}', '*', '%']) >= 0
            || CustomCssRequestPath.Any(character =>
                char.IsWhiteSpace(character)
                || char.IsControl(character)))
        {
            throw new InvalidOperationException(
                "The custom CSS request path must be a literal absolute path of at most 256 characters, without a trailing slash, empty or dot segments, route parameters, escaping, whitespace, a query or a fragment.");
        }

        if (CustomCssFilePath is not null
            && string.IsNullOrWhiteSpace(CustomCssFilePath))
        {
            throw new InvalidOperationException(
                "The custom CSS file path cannot be empty.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            AuthenticationCookieName);
        if (!SecureCookies
            && AuthenticationCookieName.StartsWith(
                "__Host-",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "__Host- UI cookies must always be Secure.");
        }
    }

    internal void ValidateRouteCollisions(
        Skopka.Hello.HelloUiRoutePaths? routes)
    {
        Validate();
        if (UsesReservedNamespace(CustomCssRequestPath)
            || string.Equals(
                CustomCssRequestPath,
                HelloUiDefaults.BuiltInStylesheetPath,
                StringComparison.OrdinalIgnoreCase)
            || routes is not null
            && GetUiRoutes(routes).Contains(
                CustomCssRequestPath,
                StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The custom CSS request path collides with a reserved Skopka.Hello endpoint.");
        }
    }

    internal void ValidateRoutes(
        Skopka.Hello.HelloUiRoutePaths routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        Validate();

        if (AuthenticatedRedirectPath is not null
            && String.Equals(
                AuthenticatedRedirectPath,
                routes.LoginPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "AuthenticatedRedirectPath cannot point to the Login page.");
        }
    }

    internal bool IsEnabled(HelloUiPages pages)
        => (EnabledPages & pages) == pages;

    internal string GetAuthenticatedRedirectPath(
        Skopka.Hello.HelloUiRoutePaths routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        return AuthenticatedRedirectPath ?? routes.AccountPath;
    }

    private static bool UsesReservedNamespace(string path)
    {
        var separator = path.IndexOf('/', 1);
        var firstSegment = separator < 0
            ? path
            : path[..separator];
        return firstSegment.Equals("/auth", StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals("/account", StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals("/health", StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals("/swagger", StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals("/openapi", StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals(
                "/signin-skopka-oidc",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLocalAbsolutePath(string path)
        => !String.IsNullOrWhiteSpace(path)
            && path.Length <= 2048
            && path.StartsWith('/')
            && !path.StartsWith("//", StringComparison.Ordinal)
            && !path.StartsWith("/\\", StringComparison.Ordinal)
            && !path.Contains("//", StringComparison.Ordinal)
            && !path.Contains("/./", StringComparison.Ordinal)
            && !path.Contains("/../", StringComparison.Ordinal)
            && !path.EndsWith("/.", StringComparison.Ordinal)
            && !path.EndsWith("/..", StringComparison.Ordinal)
            && path.IndexOfAny(['?', '#', '\\', '%']) < 0
            && !path.Any(character =>
                Char.IsWhiteSpace(character)
                || Char.IsControl(character));

    private static bool IsSafeApplicationHomeUrl(string value)
    {
        if (IsLocalAbsolutePath(value))
        {
            return true;
        }

        if (value.Length > 2048
            || value.Any(character =>
                Char.IsWhiteSpace(character)
                || Char.IsControl(character))
            || value.Contains('\\')
            || value.Contains('%')
            || value.Contains("/./", StringComparison.Ordinal)
            || value.Contains("/../", StringComparison.Ordinal)
            || value.EndsWith("/.", StringComparison.Ordinal)
            || value.EndsWith("/..", StringComparison.Ordinal)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !String.Equals(
                uri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
            || String.IsNullOrWhiteSpace(uri.Host)
            || !String.IsNullOrEmpty(uri.UserInfo)
            || !String.IsNullOrEmpty(uri.Query)
            || !String.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        return IsLocalAbsolutePath(uri.AbsolutePath);
    }

    private static void ValidateLegalDocumentUrl(
        string? value,
        string optionName)
    {
        if (value is not null && !IsSafeApplicationHomeUrl(value))
        {
            throw new InvalidOperationException(
                $"{optionName} must be a local absolute path or an absolute HTTPS URL without credentials, a query, a fragment or unsafe path segments.");
        }
    }

    private static void ValidateLayoutPath(string? value)
    {
        if (value is null)
        {
            return;
        }

        if (value == "/"
            || value.EndsWith('/')
            || value.IndexOfAny(['{', '}', '*']) >= 0
            || !IsLocalAbsolutePath(value))
        {
            throw new InvalidOperationException(
                "LayoutPath must be a local absolute Razor layout path without an authority, query, fragment, escaping, whitespace, route syntax or dot segments.");
        }
    }

    private static string[] GetUiRoutes(
        Skopka.Hello.HelloUiRoutePaths routes)
        =>
        [
            routes.RootPath,
            routes.LoginPath,
            routes.RegisterPath,
            routes.ForgotPasswordPath,
            routes.ResetPasswordPath,
            routes.ResendConfirmationPath,
            routes.ResendPhoneConfirmationPath,
            routes.ConfirmEmailPath,
            routes.ConfirmPhonePath,
            routes.AccountPath,
            routes.SessionsPath,
            routes.ChangePasswordPath,
            routes.AccountSecurityPath,
            routes.ExternalCompletionPath,
            routes.ExternalRegistrationPath,
            routes.ExternalLoginsPath,
            routes.CrossDeviceWaitingPath,
            routes.CrossDeviceApprovalPath,
            routes.CulturePath,
            routes.ErrorPath,
        ];
}
