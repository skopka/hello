using OpenIddict.Abstractions;

namespace Skopka.Hello.AuthorizationServer;

internal sealed class HelloAuthorizationClientSynchronizer(
    IOpenIddictApplicationManager applications,
    HelloAuthorizationServerOptions options) : IHelloAuthorizationClientSynchronizer
{
    public async Task SynchronizeAsync(CancellationToken cancellationToken)
    {
        var existing = new Dictionary<
            string, (object Application, OpenIddictApplicationDescriptor Descriptor)>();
        await foreach (var application in applications.ListAsync(null, null, cancellationToken))
        {
            var descriptor = await HelloClientRegistration.DescribeAsync(
                applications, application, cancellationToken);
            if (descriptor.ClientId is not null)
            {
                existing[descriptor.ClientId] = (application, descriptor);
            }
        }

        // Check every collision before the first write.
        foreach (var client in options.Clients)
        {
            if (existing.TryGetValue(client.ClientId, out var entry)
                && entry.Descriptor.Properties.ContainsKey(HelloClientRegistration.Key)
                && HelloClientRegistration.Read(entry.Descriptor)?.Ownership != "configuration")
            {
                throw new InvalidOperationException(
                    $"Authorization client '{client.ClientId}' is not configuration-owned.");
            }
        }

        var configured = options.Clients.Select(x => x.ClientId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var entry in existing.Values)
        {
            if (HelloClientRegistration.Read(entry.Descriptor)?.Ownership == "configuration"
                && !configured.Contains(entry.Descriptor.ClientId!))
            {
                await applications.DeleteAsync(entry.Application, cancellationToken);
            }
        }

        foreach (var client in options.Clients)
        {
            var found = existing.TryGetValue(client.ClientId, out var entry);
            var descriptor = found ? entry.Descriptor : new OpenIddictApplicationDescriptor();
            var previous = HelloClientRegistration.Read(descriptor);
            var secretMatches = client.Type != HelloAuthorizationClientType.Confidential
                || (found && await applications.ValidateClientSecretAsync(
                    entry.Application, client.ClientSecret!, cancellationToken));
            if (found && previous is not null && secretMatches
                && Matches(client, descriptor, previous))
            {
                continue;
            }

            HelloClientRegistration.Apply(descriptor, client, new HelloClientMetadata(
                "configuration", (previous?.Revision ?? 0) + 1,
                options.GetResource(client), client.HonorReauthenticationPrompts,
                client.Scopes.ToArray()));
            if (client.Type == HelloAuthorizationClientType.Confidential && !secretMatches)
            {
                descriptor.ClientSecret = client.ClientSecret;
            }
            else if (client.Type == HelloAuthorizationClientType.Public)
            {
                descriptor.ClientSecret = null;
            }

            if (found)
            {
                await applications.UpdateAsync(entry.Application, descriptor, cancellationToken);
            }
            else
            {
                await applications.CreateAsync(descriptor, cancellationToken);
            }
        }
    }

    private bool Matches(
        HelloAuthorizationClientOptions client,
        OpenIddictApplicationDescriptor descriptor,
        HelloClientMetadata previous)
    {
        var type = client.Type == HelloAuthorizationClientType.Confidential
            ? OpenIddictConstants.ClientTypes.Confidential
            : OpenIddictConstants.ClientTypes.Public;
        var expected = new OpenIddictApplicationDescriptor();
        HelloClientRegistration.Apply(expected, client, previous);
        return previous.Resource == options.GetResource(client)
            && previous.HonorReauthenticationPrompts == client.HonorReauthenticationPrompts
            && previous.Scopes is not null
            && previous.Scopes.SequenceEqual(client.Scopes, StringComparer.Ordinal)
            && descriptor.DisplayName == client.DisplayName
            && descriptor.ClientType == type
            && descriptor.ConsentType == expected.ConsentType
            && descriptor.Permissions.SetEquals(expected.Permissions)
            && descriptor.Requirements.SetEquals(expected.Requirements)
            && descriptor.RedirectUris.Select(x => x.OriginalString)
                .ToHashSet(StringComparer.Ordinal).SetEquals(client.RedirectUris)
            && descriptor.PostLogoutRedirectUris.Select(x => x.OriginalString)
                .ToHashSet(StringComparer.Ordinal).SetEquals(client.PostLogoutRedirectUris);
    }
}
