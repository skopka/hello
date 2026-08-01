using Microsoft.AspNetCore.Http.HttpResults;

namespace Skopka.Hello.Oidc;

internal static class HelloOidcReturnUrl
{
    private const int MaximumLength = 2_048;

    public static string Normalize(
        string? returnUrl,
        string fallback)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)
            || returnUrl.Length > MaximumLength
            || !RedirectHttpResult.IsLocalUrl(returnUrl)
            || returnUrl.Contains('\\', StringComparison.Ordinal)
            || returnUrl.StartsWith(
                HelloOidcDefaults.CallbackPathPrefix,
                StringComparison.OrdinalIgnoreCase)
            || returnUrl.StartsWith(
                HelloOidcDefaults.CompletionPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        return returnUrl;
    }
}
