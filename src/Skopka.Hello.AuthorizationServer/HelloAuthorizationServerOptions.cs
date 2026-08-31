using OpenIddict.Abstractions;

namespace Skopka.Hello.AuthorizationServer;

public sealed class HelloAuthorizationServerOptions
{
    private const int MaximumClients = 64;
    private const int MaximumRedirectUris = 16;
    private const int MaximumAdditionalScopes = 32;
    private const int MaximumScopeLength = 128;
    private const int MaximumResourceLength = 256;

    public Uri? Issuer { get; set; }

    public string AuthorizationEndpointPath { get; set; } =
        HelloAuthorizationDefaults.AuthorizationEndpointPath;

    public string TokenEndpointPath { get; set; } =
        HelloAuthorizationDefaults.TokenEndpointPath;

    public string EndSessionEndpointPath { get; set; } =
        HelloAuthorizationDefaults.EndSessionEndpointPath;

    public string BrowserAuthenticationScheme { get; set; } =
        "Skopka.Hello.UI";

    public string IdentityBearerAuthenticationScheme { get; set; } =
        "Bearer";

    public string CompositeBearerAuthenticationScheme { get; set; } =
        HelloAuthorizationDefaults.CompositeBearerAuthenticationScheme;

    public string Resource { get; set; } = "skopka-hello-api";

    public HelloAuthorizationAccessTokenFormat AccessTokenFormat { get; set; } =
        HelloAuthorizationAccessTokenFormat.Reference;

    public List<string> AdditionalScopes { get; set; } = [];

    public TimeSpan AuthorizationCodeLifetime { get; set; } =
        TimeSpan.FromMinutes(5);

    public TimeSpan AccessTokenLifetime { get; set; } =
        TimeSpan.FromMinutes(15);

    public TimeSpan IdentityTokenLifetime { get; set; } =
        TimeSpan.FromMinutes(5);

    public TimeSpan RefreshTokenLifetime { get; set; } =
        TimeSpan.FromDays(30);

    public bool DisableTransportSecurityRequirement { get; set; }

    public bool AccountSelectionEnabled { get; set; }

    public string AccountSelectionPath { get; set; } =
        "/hello/accounts";

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
        ValidatePath(EndSessionEndpointPath, nameof(EndSessionEndpointPath));
        if (AccountSelectionEnabled)
        {
            ValidatePath(
                AccountSelectionPath,
                nameof(AccountSelectionPath));
        }
        var endpointPaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            AuthorizationEndpointPath,
            TokenEndpointPath,
            EndSessionEndpointPath,
        };
        if (AccountSelectionEnabled)
        {
            endpointPaths.Add(AccountSelectionPath);
        }

        var expectedEndpointCount = AccountSelectionEnabled ? 4 : 3;
        if (endpointPaths.Count != expectedEndpointCount)
        {
            throw new InvalidOperationException(
                "Authorization, token, end-session and account-selection paths must be different.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            BrowserAuthenticationScheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            IdentityBearerAuthenticationScheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            CompositeBearerAuthenticationScheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(Resource);
        ValidateResource(Resource, "The default authorization resource");

        if (!Enum.IsDefined(AccessTokenFormat))
        {
            throw new InvalidOperationException(
                "The authorization access-token format is invalid.");
        }

        if (AdditionalScopes is null)
        {
            throw new InvalidOperationException(
                "The additional authorization scopes collection is required.");
        }

        if (AdditionalScopes.Count > MaximumAdditionalScopes)
        {
            throw new InvalidOperationException(
                $"No more than {MaximumAdditionalScopes} additional authorization scopes are supported.");
        }

        var supportedScopes = new HashSet<string>(
            BuiltInScopes,
            StringComparer.Ordinal);
        foreach (var scope in AdditionalScopes)
        {
            if (!IsValidScopeName(scope) || !supportedScopes.Add(scope))
            {
                throw new InvalidOperationException(
                    "An additional authorization scope is invalid, duplicated or reserved.");
            }
        }

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
            ValidateClient(client, ids, supportedScopes);
        }
    }

    internal string GetOpenIddictAuthorizationEndpointPath()
        => AuthorizationEndpointPath.TrimStart('/');

    internal string GetOpenIddictTokenEndpointPath()
        => TokenEndpointPath.TrimStart('/');

    internal string GetOpenIddictEndSessionEndpointPath()
        => EndSessionEndpointPath.TrimStart('/');

    internal string GetResource(HelloAuthorizationClientOptions client)
        => string.IsNullOrWhiteSpace(client.Resource)
            ? Resource
            : client.Resource;

    internal string[] GetResources()
        => Clients
            .Select(GetResource)
            .Append(Resource)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    internal string[] GetScopes()
        => BuiltInScopes
            .Concat(AdditionalScopes)
            .ToArray();

    private void ValidateClient(
        HelloAuthorizationClientOptions client,
        HashSet<string> ids,
        HashSet<string> supportedScopes)
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

        if (client.Resource is not null)
        {
            ValidateResource(
                client.Resource,
                $"Authorization client '{client.ClientId}' resource");
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

        if (client.PostLogoutRedirectUris is null
            || client.PostLogoutRedirectUris.Count > MaximumRedirectUris)
        {
            throw new InvalidOperationException(
                $"Authorization client '{client.ClientId}' cannot have more than {MaximumRedirectUris} post-logout redirect URIs.");
        }

        foreach (var value in client.PostLogoutRedirectUris)
        {
            ValidateRedirectUri(client, value, "post-logout redirect URI");
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
                || !supportedScopes.Contains(scope)
                || !scopes.Add(scope))
            {
                throw new InvalidOperationException(
                    $"Authorization client '{client.ClientId}' has an invalid or duplicate scope.");
            }
        }
    }

    private void ValidateRedirectUri(
        HelloAuthorizationClientOptions client,
        string value,
        string kind = "redirect URI")
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.UserInfo.Length > 0
            || uri.Fragment.Length > 0)
        {
            throw new InvalidOperationException(
                $"Authorization client '{client.ClientId}' has an invalid {kind}.");
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
            $"Authorization client '{client.ClientId}' has an unsafe {kind}.");
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

    private static void ValidateResource(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumResourceLength
            || value.Any(character => char.IsWhiteSpace(character)
                || char.IsControl(character)))
        {
            throw new InvalidOperationException(
                $"{name} must be a non-empty value without whitespace or control characters.");
        }
    }

    private static bool IsValidScopeName(string? scope)
    {
        if (string.IsNullOrEmpty(scope)
            || scope.Length > MaximumScopeLength)
        {
            return false;
        }

        foreach (var character in scope)
        {
            if (character is not ('\u0021')
                && character is not (>= '\u0023' and <= '\u005B')
                && character is not (>= '\u005D' and <= '\u007E'))
            {
                return false;
            }
        }

        return true;
    }

    private static readonly string[] BuiltInScopes =
    [
        OpenIddictConstants.Scopes.OpenId,
        OpenIddictConstants.Scopes.OfflineAccess,
        OpenIddictConstants.Scopes.Profile,
        OpenIddictConstants.Scopes.Email,
        OpenIddictConstants.Scopes.Phone,
        HelloAuthorizationDefaults.RolesScope,
    ];
}
