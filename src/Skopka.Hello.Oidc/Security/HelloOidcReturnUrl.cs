using Microsoft.AspNetCore.Http.HttpResults;

namespace Skopka.Hello.Oidc;

internal static class HelloOidcReturnUrl
{
    private const int MaximumLength = 2_048;

    public static string Normalize(
        string? returnUrl,
        string fallback,
        string completionPath)
    {
        if (!IsValid(returnUrl, completionPath))
        {
            return fallback;
        }

        return returnUrl!;
    }

    public static bool TryNormalizeHeadless(
        string? returnUrl,
        out string normalized)
    {
        if (!IsValid(
                returnUrl,
                HelloOidcDefaults.ApiCompletionPath)
            || !returnUrl!.StartsWith('/')
            || returnUrl!.StartsWith(
                HelloOidcDefaults.ApiPathPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            normalized = String.Empty;
            return false;
        }

        normalized = returnUrl;
        return true;
    }

    public static string AppendExternalError(string returnUrl)
    {
        var fragmentIndex = returnUrl.IndexOf('#');
        var pathAndQuery = fragmentIndex < 0
            ? returnUrl
            : returnUrl[..fragmentIndex];
        var fragment = fragmentIndex < 0
            ? String.Empty
            : returnUrl[fragmentIndex..];
        var separator = pathAndQuery.Contains('?', StringComparison.Ordinal)
            ? '&'
            : '?';
        return $"{pathAndQuery}{separator}externalError=true{fragment}";
    }

    private static bool IsValid(
        string? returnUrl,
        string completionPath)
        => !String.IsNullOrWhiteSpace(returnUrl)
            && returnUrl.Length <= MaximumLength
            && RedirectHttpResult.IsLocalUrl(returnUrl)
            && !returnUrl.Contains('\\', StringComparison.Ordinal)
            && !returnUrl.StartsWith(
                HelloOidcDefaults.CallbackPathPrefix,
                StringComparison.OrdinalIgnoreCase)
            && !returnUrl.StartsWith(
                completionPath,
                StringComparison.OrdinalIgnoreCase);
}
