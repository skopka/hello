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

        if (!CustomCssRequestPath.StartsWith('/')
            || CustomCssRequestPath.Contains('?')
            || CustomCssRequestPath.Contains('#'))
        {
            throw new InvalidOperationException(
                "The custom CSS request path must be an absolute path without a query or fragment.");
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
}
