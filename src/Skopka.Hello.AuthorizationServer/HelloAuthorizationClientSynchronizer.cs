using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Skopka.Hello.AuthorizationServer;

internal sealed class HelloAuthorizationClientSynchronizer(
    IOpenIddictApplicationManager applications,
    HelloAuthorizationServerOptions options)
    : IHelloAuthorizationClientSynchronizer
{
    public async Task SynchronizeAsync(
        CancellationToken cancellationToken)
    {
        var configuredClientIds = options.Clients
            .Select(client => client.ClientId)
            .ToHashSet(StringComparer.Ordinal);
        var applicationIdsToDelete = new List<string>();
        await foreach (var application in applications.ListAsync(
            null,
            null,
            cancellationToken))
        {
            var clientId = await applications.GetClientIdAsync(
                application,
                cancellationToken);
            if (clientId is null
                || !configuredClientIds.Contains(clientId))
            {
                var applicationId = await applications.GetIdAsync(
                    application,
                    cancellationToken);
                if (applicationId is not null)
                {
                    applicationIdsToDelete.Add(applicationId);
                }
            }
        }

        foreach (var applicationId in applicationIdsToDelete)
        {
            var application = await applications.FindByIdAsync(
                applicationId,
                cancellationToken);
            if (application is null)
            {
                continue;
            }

            await applications.DeleteAsync(
                application,
                cancellationToken);
        }

        foreach (var client in options.Clients)
        {
            var descriptor = CreateDescriptor(client);
            var existing = await applications.FindByClientIdAsync(
                client.ClientId,
                cancellationToken);
            if (existing is null)
            {
                await applications.CreateAsync(
                    descriptor,
                    cancellationToken);
            }
            else
            {
                await applications.UpdateAsync(
                    existing,
                    descriptor,
                    cancellationToken);
            }
        }
    }

    private static OpenIddictApplicationDescriptor CreateDescriptor(
        HelloAuthorizationClientOptions client)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = client.ClientId,
            ClientSecret = client.Type
                == HelloAuthorizationClientType.Confidential
                    ? client.ClientSecret
                    : null,
            ClientType = client.Type
                == HelloAuthorizationClientType.Confidential
                    ? ClientTypes.Confidential
                    : ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = client.DisplayName,
        };

        descriptor.RedirectUris.UnionWith(
            client.RedirectUris.Select(uri => new Uri(uri, UriKind.Absolute)));
        descriptor.Permissions.UnionWith(
        [
            Permissions.Endpoints.Authorization,
            Permissions.Endpoints.Token,
            Permissions.GrantTypes.AuthorizationCode,
            Permissions.GrantTypes.RefreshToken,
            Permissions.ResponseTypes.Code,
        ]);
        descriptor.Permissions.UnionWith(
            client.Scopes
                .Where(scope => scope is not Scopes.OpenId
                    and not Scopes.OfflineAccess)
                .Select(scope => Permissions.Prefixes.Scope + scope));
        descriptor.Requirements.Add(
            Requirements.Features.ProofKeyForCodeExchange);
        return descriptor;
    }
}
