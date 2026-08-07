using Microsoft.AspNetCore.Http;

namespace Skopka.Hello.Oidc;

public sealed class HelloOidcOptions
{
    public Uri? PublicOrigin { get; set; }

    public bool SecureCookies { get; set; } = true;

    public string ExternalCookieName { get; set; } =
        HelloOidcDefaults.ExternalCookieName;

    public TimeSpan ExternalCookieLifetime { get; set; } =
        TimeSpan.FromMinutes(5);

    public string PendingCookieName { get; set; } =
        HelloOidcDefaults.PendingCookieName;

    public string LinkRequestCookieName { get; set; } =
        HelloOidcDefaults.LinkRequestCookieName;

    public TimeSpan PendingCookieLifetime { get; set; } =
        TimeSpan.FromMinutes(10);

    public bool PasswordSignInEnabled { get; set; } = true;

    public IDictionary<string, HelloOidcProviderOptions> Providers { get; } =
        new Dictionary<string, HelloOidcProviderOptions>(
            StringComparer.OrdinalIgnoreCase);

    internal IReadOnlyList<HelloOidcProviderRegistration> Validate()
    {
        if (PublicOrigin is null
            || !PublicOrigin.IsAbsoluteUri
            || (PublicOrigin.Scheme != Uri.UriSchemeHttps
                && PublicOrigin.Scheme != Uri.UriSchemeHttp)
            || PublicOrigin.UserInfo.Length > 0
            || PublicOrigin.Query.Length > 0
            || PublicOrigin.Fragment.Length > 0
            || PublicOrigin.AbsolutePath != "/")
        {
            throw new InvalidOperationException(
                "PublicOrigin must be an absolute HTTP(S) origin without credentials, path, query or fragment.");
        }

        if (ExternalCookieLifetime < TimeSpan.FromMinutes(1)
            || ExternalCookieLifetime > TimeSpan.FromMinutes(30))
        {
            throw new InvalidOperationException(
                "ExternalCookieLifetime must be between one and thirty minutes.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ExternalCookieName);
        ArgumentException.ThrowIfNullOrWhiteSpace(PendingCookieName);
        ArgumentException.ThrowIfNullOrWhiteSpace(LinkRequestCookieName);
        if (new[]
            {
                ExternalCookieName,
                PendingCookieName,
                LinkRequestCookieName,
            }.Distinct(StringComparer.Ordinal).Count() != 3)
        {
            throw new InvalidOperationException(
                "External, pending and link-request OIDC cookies require different names.");
        }

        if (!SecureCookies
            && new[]
                {
                    ExternalCookieName,
                    PendingCookieName,
                    LinkRequestCookieName,
                }.Any(
                name => name.StartsWith(
                    "__Host-",
                    StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "__Host- OIDC cookies must always be Secure.");
        }

        if (PendingCookieLifetime < TimeSpan.FromMinutes(1)
            || PendingCookieLifetime > TimeSpan.FromMinutes(30))
        {
            throw new InvalidOperationException(
                "PendingCookieLifetime must be between one and thirty minutes.");
        }

        var registrations = new List<HelloOidcProviderRegistration>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var callbacks = new HashSet<PathString>();
        foreach (var pair in Providers)
        {
            if (!pair.Value.Enabled)
            {
                continue;
            }

            var id = NormalizeProviderId(pair.Key);
            if (!ids.Add(id))
            {
                throw new InvalidOperationException(
                    $"OIDC provider id '{id}' is configured more than once.");
            }

            var provider = pair.Value;
            ValidateProvider(id, provider);
            var callbackPath = new PathString(
                HelloOidcDefaults.CallbackPathPrefix + id);
            if (!callbacks.Add(callbackPath))
            {
                throw new InvalidOperationException(
                    $"OIDC callback path '{callbackPath}' is configured more than once.");
            }

            registrations.Add(
                new HelloOidcProviderRegistration(
                    id,
                    provider.DisplayName.Trim(),
                    provider.Authority.TrimEnd('/'),
                    provider.ClientId,
                    provider.ClientSecret,
                    provider.RequireHttpsMetadata,
                    provider.Order,
                    NormalizeScopes(provider.Scopes),
                    HelloOidcDefaults.ProviderSchemePrefix + id,
                    callbackPath));
        }

        return registrations
            .OrderBy(provider => provider.Order)
            .ThenBy(
                provider => provider.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeProviderId(string? value)
    {
        var id = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (id.Length is < 1 or > 64
            || !char.IsAsciiLetterOrDigit(id[0])
            || id.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_' and not '.'))
        {
            throw new InvalidOperationException(
                "OIDC provider ids must contain 1-64 ASCII letters, digits, '.', '_' or '-', and start with a letter or digit.");
        }

        return id;
    }

    private void ValidateProvider(
        string id,
        HelloOidcProviderOptions provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (string.IsNullOrWhiteSpace(provider.DisplayName)
            || provider.DisplayName.Trim().Length > 100)
        {
            throw new InvalidOperationException(
                $"OIDC provider '{id}' requires a display name of at most 100 characters.");
        }

        if (!Uri.TryCreate(
                provider.Authority,
                UriKind.Absolute,
                out var authority)
            || (authority.Scheme != Uri.UriSchemeHttps
                && authority.Scheme != Uri.UriSchemeHttp)
            || authority.UserInfo.Length > 0
            || authority.Query.Length > 0
            || authority.Fragment.Length > 0)
        {
            throw new InvalidOperationException(
                $"OIDC provider '{id}' requires an absolute HTTP(S) authority without credentials, query or fragment.");
        }

        if (authority.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"OIDC provider '{id}' requires an HTTPS authority.");
        }

        if (!provider.RequireHttpsMetadata)
        {
            throw new InvalidOperationException(
                $"OIDC provider '{id}' requires HTTPS metadata validation.");
        }

        if (!SecureCookies
            || PublicOrigin!.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Enabled OIDC providers require SecureCookies and an HTTPS PublicOrigin.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(provider.ClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider.ClientSecret);
    }

    private static string[] NormalizeScopes(
        IEnumerable<string> configured)
    {
        var scopes = new HashSet<string>(StringComparer.Ordinal)
        {
            "openid",
            "profile",
            "email",
        };
        foreach (var value in configured)
        {
            var scope = value?.Trim() ?? string.Empty;
            if (scope.Length is < 1 or > 200
                || scope.Any(char.IsWhiteSpace))
            {
                throw new InvalidOperationException(
                    "OIDC scopes must be non-empty, at most 200 characters and contain no whitespace.");
            }

            scopes.Add(scope);
        }

        return scopes.ToArray();
    }
}

internal sealed record HelloOidcProviderRegistration(
    string Id,
    string DisplayName,
    string Authority,
    string ClientId,
    string ClientSecret,
    bool RequireHttpsMetadata,
    int Order,
    IReadOnlyList<string> Scopes,
    string AuthenticationScheme,
    PathString CallbackPath);
