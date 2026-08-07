using OpenIddict.Abstractions;

namespace Skopka.Hello.AuthorizationServer;

public sealed class HelloAuthorizationServerOptions
{
    private const int MaximumClients = 64;
    private const int MaximumRedirectUris = 16;

    public Uri? Issuer { get; set; }

    public string AuthorizationEndpointPath { get; set; } =
        HelloAuthorizationDefaults.AuthorizationEndpointPath;

    public string TokenEndpointPath { get; set; } =
        HelloAuthorizationDefaults.TokenEndpointPath;

    public string BrowserAuthenticationScheme { get; set; } =
        "Skopka.Hello.UI";

    public string IdentityBearerAuthenticationScheme { get; set; } =
        "Bearer";

    public string CompositeBearerAuthenticationScheme { get; set; } =
        HelloAuthorizationDefaults.CompositeBearerAuthenticationScheme;

    public string Resource { get; set; } = "skopka-hello-api";

    public TimeSpan AuthorizationCodeLifetime { get; set; } =
        TimeSpan.FromMinutes(5);

    public TimeSpan AccessTokenLifetime { get; set; } =
        TimeSpan.FromMinutes(15);

    public TimeSpan IdentityTokenLifetime { get; set; } =
        TimeSpan.FromMinutes(5);

    public TimeSpan RefreshTokenLifetime { get; set; } =
        TimeSpan.FromDays(30);

    public bool DisableTransportSecurityRequirement { get; set; }

    public List<HelloAuthorizationClientOptions> Clients { get; set; } = [];

    public void Validate()
    {
        if (Issuer is null
            || !Issuer.IsAbsoluteUri
            || (Issuer.Scheme != Uri.UriSchemeHttps
                && !(DisableTransportSecurityRequirement
                    && Issuer.Scheme == Uri.UriSchemeHttp))
            || Issuer.UserInfo.Length > 0
            || Issuer.Query.Length > 0
            || Issuer.Fragment.Length > 0)
        {
            throw new InvalidOperationException(
                "Issuer must be an absolute HTTPS URI without credentials, query or fragment.");
        }

        ValidatePath(AuthorizationEndpointPath, nameof(AuthorizationEndpointPath));
        ValidatePath(TokenEndpointPath, nameof(TokenEndpointPath));
        if (string.Equals(
                AuthorizationEndpointPath,
                TokenEndpointPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Authorization and token endpoint paths must be different.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            BrowserAuthenticationScheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            IdentityBearerAuthenticationScheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            CompositeBearerAuthenticationScheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(Resource);

        if (AuthorizationCodeLifetime <= TimeSpan.Zero
            || AccessTokenLifetime <= TimeSpan.Zero
            || IdentityTokenLifetime <= TimeSpan.Zero
            || RefreshTokenLifetime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Authorization-server token lifetimes must be positive.");
        }

        if (Clients is null)
        {
            throw new InvalidOperationException(
                "Authorization clients collection is required.");
        }

        if (Clients.Count > MaximumClients)
        {
            throw new InvalidOperationException(
                $"No more than {MaximumClients} authorization clients are supported.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var client in Clients)
        {
            ValidateClient(client, ids);
        }
    }

    internal string GetOpenIddictAuthorizationEndpointPath()
        => AuthorizationEndpointPath.TrimStart('/');

    internal string GetOpenIddictTokenEndpointPath()
        => TokenEndpointPath.TrimStart('/');

    private void ValidateClient(
        HelloAuthorizationClientOptions client,
        HashSet<string> ids)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(client.ClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(client.DisplayName);
        if (client.ClientId.Length > 128 || client.DisplayName.Length > 256)
        {
            throw new InvalidOperationException(
                "Authorization client ids and display names are too long.");
        }

        if (!ids.Add(client.ClientId))
        {
            throw new InvalidOperationException(
                $"Authorization client id '{client.ClientId}' is duplicated.");
        }

        if (!Enum.IsDefined(client.Type))
        {
            throw new InvalidOperationException(
                $"Authorization client '{client.ClientId}' has an invalid type.");
        }

        if (client.Type == HelloAuthorizationClientType.Public
            && !string.IsNullOrEmpty(client.ClientSecret))
        {
            throw new InvalidOperationException(
                $"Public client '{client.ClientId}' cannot have a secret.");
        }

        if (client.Type == HelloAuthorizationClientType.Confidential
            && string.IsNullOrWhiteSpace(client.ClientSecret))
        {
            throw new InvalidOperationException(
                $"Confidential client '{client.ClientId}' requires a secret.");
        }

        if (client.RedirectUris is not { Count: > 0 }
            || client.RedirectUris.Count > MaximumRedirectUris)
        {
            throw new InvalidOperationException(
                $"Authorization client '{client.ClientId}' must have between 1 and {MaximumRedirectUris} redirect URIs.");
        }

        foreach (var value in client.RedirectUris)
        {
            ValidateRedirectUri(client, value);
        }

        if (client.Scopes is null)
        {
            throw new InvalidOperationException(
                $"Authorization client '{client.ClientId}' requires a scope collection.");
        }

        var scopes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scope in client.Scopes)
        {
            if (string.IsNullOrWhiteSpace(scope)
                || !SupportedScopes.Contains(scope)
                || !scopes.Add(scope))
            {
                throw new InvalidOperationException(
                    $"Authorization client '{client.ClientId}' has an invalid or duplicate scope.");
            }
        }
    }

    private void ValidateRedirectUri(
        HelloAuthorizationClientOptions client,
        string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.UserInfo.Length > 0
            || uri.Fragment.Length > 0)
        {
            throw new InvalidOperationException(
                $"Authorization client '{client.ClientId}' has an invalid redirect URI.");
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return;
        }

        if (uri.Scheme == Uri.UriSchemeHttp
            && (uri.IsLoopback || DisableTransportSecurityRequirement))
        {
            return;
        }

        if (client.Type == HelloAuthorizationClientType.Public
            && uri.Scheme.Contains('.', StringComparison.Ordinal)
            && uri.Host.Length == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Authorization client '{client.ClientId}' has an unsafe redirect URI.");
    }

    private static void ValidatePath(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value[0] != '/'
            || value.Length > 256
            || value.Contains('?')
            || value.Contains('#')
            || value.EndsWith('/'))
        {
            throw new InvalidOperationException(
                $"{name} must be a root-relative endpoint path without a trailing slash, query or fragment.");
        }
    }

    private static readonly HashSet<string> SupportedScopes = new(
        StringComparer.Ordinal)
    {
        OpenIddictConstants.Scopes.OpenId,
        OpenIddictConstants.Scopes.OfflineAccess,
        OpenIddictConstants.Scopes.Profile,
        OpenIddictConstants.Scopes.Email,
        OpenIddictConstants.Scopes.Phone,
        HelloAuthorizationDefaults.RolesScope,
    };
}
