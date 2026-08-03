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

    public void Validate()
    {
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
                "/_content/Skopka.Hello.UI/css/hello.css",
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
            routes.ExternalCompletionPath,
            routes.ExternalRegistrationPath,
            routes.ExternalLoginsPath,
        ];
}
