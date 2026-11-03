using System.Text.Json;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Skopka.Hello.AuthorizationServer;

internal sealed record HelloClientMetadata(
    string Ownership,
    long Revision,
    string Resource,
    bool HonorReauthenticationPrompts,
    string[] Scopes);

internal static class HelloClientRegistration
{
    internal const string Key = "skopka_hello_client";

    internal static HelloClientMetadata? Read(OpenIddictApplicationDescriptor descriptor)
    {
        if (!descriptor.Properties.TryGetValue(Key, out var value))
        {
            return null;
        }

        try
        {
            var metadata = value.Deserialize<HelloClientMetadata>();
            return metadata is not null
                && metadata.Ownership is "configuration" or "administration"
                && metadata.Revision > 0
                && !string.IsNullOrWhiteSpace(metadata.Resource)
                && metadata.Scopes is not null
                && metadata.Scopes.All(scope => !string.IsNullOrWhiteSpace(scope))
                    ? metadata
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static void Write(
        OpenIddictApplicationDescriptor descriptor,
        HelloClientMetadata metadata)
        => descriptor.Properties[Key] = JsonSerializer.SerializeToElement(metadata);

    internal static void Apply(
        OpenIddictApplicationDescriptor descriptor,
        HelloAuthorizationClientOptions client,
        HelloClientMetadata metadata)
    {
        descriptor.ClientId = client.ClientId;
        descriptor.ClientType = client.Type == HelloAuthorizationClientType.Confidential
            ? ClientTypes.Confidential
            : ClientTypes.Public;
        descriptor.ConsentType = ConsentTypes.Implicit;
        descriptor.DisplayName = client.DisplayName;
        descriptor.RedirectUris.Clear();
        descriptor.RedirectUris.UnionWith(
            client.RedirectUris.Select(x => new Uri(x, UriKind.Absolute)));
        descriptor.PostLogoutRedirectUris.Clear();
        descriptor.PostLogoutRedirectUris.UnionWith(
            client.PostLogoutRedirectUris.Select(x => new Uri(x, UriKind.Absolute)));
        descriptor.Permissions.Clear();
        descriptor.Permissions.UnionWith(
        [
            Permissions.Endpoints.Authorization,
            Permissions.Endpoints.Token,
            Permissions.GrantTypes.AuthorizationCode,
            Permissions.GrantTypes.RefreshToken,
            Permissions.ResponseTypes.Code,
        ]);
        if (client.PostLogoutRedirectUris.Count > 0)
        {
            descriptor.Permissions.Add(Permissions.Endpoints.EndSession);
        }

        descriptor.Permissions.UnionWith(client.Scopes
            .Where(x => x is not Scopes.OpenId and not Scopes.OfflineAccess)
            .Select(x => Permissions.Prefixes.Scope + x));
        descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        Write(descriptor, metadata);
    }

    internal static async Task<OpenIddictApplicationDescriptor> DescribeAsync(
        IOpenIddictApplicationManager manager,
        object application,
        CancellationToken cancellationToken)
    {
        var descriptor = new OpenIddictApplicationDescriptor();
        await manager.PopulateAsync(descriptor, application, cancellationToken);
        return descriptor;
    }
}
