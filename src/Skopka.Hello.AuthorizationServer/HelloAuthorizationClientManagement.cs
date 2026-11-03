using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using OpenIddict.Abstractions;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.AuthorizationServer;

internal sealed class HelloAuthorizationClientManagement(
    IOpenIddictApplicationManager applications,
    HelloAuthorizationServerOptions options) : IHelloAuthorizationClientManagement
{
    public HelloAuthorizationClientCatalog GetCatalog()
        => new(options.GetScopes(), options.GetResources());

    public async Task<OperationResult<IReadOnlyList<HelloAuthorizationClientView>>> ListAsync(
        int offset, int count, CancellationToken cancellationToken)
    {
        if (offset < 0 || count is < 1 or > 100)
        {
            return Fail<IReadOnlyList<HelloAuthorizationClientView>>("Invalid page.");
        }
        var result = new List<HelloAuthorizationClientView>();
        await foreach (var app in applications.ListAsync(count, offset, cancellationToken))
        {
            result.Add(await ViewAsync(app, cancellationToken));
        }
        return OperationResultFactory.Success<IReadOnlyList<HelloAuthorizationClientView>>(result.AsReadOnly());
    }

    public async Task<OperationResult<HelloAuthorizationClientView>> GetAsync(
        string clientId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return Fail<HelloAuthorizationClientView>("Invalid client ID.");
        }
        var app = await applications.FindByClientIdAsync(clientId, cancellationToken);
        return app is null
            ? Fail<HelloAuthorizationClientView>("Client was not found.", ErrorType.NotFound)
            : OperationResultFactory.Success(await ViewAsync(app, cancellationToken));
    }

    public async Task<OperationResult<bool>> ValidateCreateAsync(
        HelloAuthorizationClientInput input, CancellationToken cancellationToken)
    {
        var error = Validate(input);
        if (error is not null)
        {
            return Fail<bool>(error);
        }
        if (options.Clients.Any(x => x.ClientId == input.ClientId)
            || await applications.FindByClientIdAsync(input.ClientId, cancellationToken) is not null)
        {
            return Fail<bool>("Client ID is already reserved.", ErrorType.Conflict);
        }

        return OperationResultFactory.Success(true);
    }

    public async Task<OperationResult<bool>> ValidateUpdateAsync(
        string clientId, long expectedRevision, HelloAuthorizationClientInput input,
        CancellationToken cancellationToken)
    {
        var error = Validate(input);
        if (error is not null)
        {
            return Fail<bool>(error);
        }
        if (string.IsNullOrWhiteSpace(clientId) || clientId != input.ClientId || expectedRevision <= 0)
        {
            return Fail<bool>("Invalid client ID or revision.");
        }
        var app = await applications.FindByClientIdAsync(clientId, cancellationToken);
        if (app is null)
        {
            return Fail<bool>("Client was not found.", ErrorType.NotFound);
        }
        var metadata = HelloClientRegistration.Read(await HelloClientRegistration.DescribeAsync(applications, app, cancellationToken));
        if (metadata?.Ownership != "administration")
        {
            return Fail<bool>("Client is read-only.", ErrorType.Forbidden);
        }

        if (expectedRevision <= 0 || metadata.Revision != expectedRevision)
        {
            return Fail<bool>("Client has changed.", ErrorType.Conflict);
        }
        return OperationResultFactory.Success(true);
    }

    public async Task<OperationResult<HelloAuthorizationClientMutation>> CreateAsync(
        HelloAuthorizationClientInput input, CancellationToken cancellationToken)
    {
        var validation = Validate(input);
        if (validation is not null)
        {
            return Fail<HelloAuthorizationClientMutation>(validation);
        }
        if (options.Clients.Any(x => x.ClientId == input.ClientId)
            || await applications.FindByClientIdAsync(input.ClientId, cancellationToken) is not null)
        {
            return Fail<HelloAuthorizationClientMutation>("Client ID is already reserved.", ErrorType.Conflict);
        }
        var secret = input.Type == HelloAuthorizationClientType.Confidential ? NewSecret() : null;
        var descriptor = new OpenIddictApplicationDescriptor { ClientSecret = secret };
        HelloClientRegistration.Apply(descriptor, ToOptions(input), new HelloClientMetadata(
            "administration", 1, input.Resource, input.HonorReauthenticationPrompts,
            input.Scopes.ToArray()));
        try
        {
            var app = await applications.CreateAsync(descriptor, cancellationToken);
            return OperationResultFactory.Success(new HelloAuthorizationClientMutation(await ViewAsync(app, cancellationToken), secret));
        }
        catch (OpenIddictExceptions.ConcurrencyException)
        {
            return Fail<HelloAuthorizationClientMutation>("Client changed concurrently.", ErrorType.Conflict);
        }
        catch (OpenIddictExceptions.ValidationException)
        {
            return Fail<HelloAuthorizationClientMutation>("Client ID is already reserved or registration is invalid.", ErrorType.Conflict);
        }
        catch (Exception exception) when (IsUniqueViolation(exception))
        {
            return Fail<HelloAuthorizationClientMutation>("Client ID is already reserved.", ErrorType.Conflict);
        }
    }

    public async Task<OperationResult<HelloAuthorizationClientMutation>> UpdateAsync(
        string clientId, long expectedRevision, HelloAuthorizationClientInput input,
        CancellationToken cancellationToken)
    {
        var validation = Validate(input);
        if (validation is not null)
        {
            return Fail<HelloAuthorizationClientMutation>(validation);
        }
        if (clientId != input.ClientId)
        {
            return Fail<HelloAuthorizationClientMutation>("Client ID cannot be changed.");
        }
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return Fail<HelloAuthorizationClientMutation>("Invalid client ID.");
        }
        var app = await applications.FindByClientIdAsync(clientId, cancellationToken);
        if (app is null)
        {
            return Fail<HelloAuthorizationClientMutation>("Client was not found.", ErrorType.NotFound);
        }
        var descriptor = await HelloClientRegistration.DescribeAsync(applications, app, cancellationToken);
        var metadata = HelloClientRegistration.Read(descriptor);
        if (metadata?.Ownership != "administration")
        {
            return Fail<HelloAuthorizationClientMutation>("Client is read-only.", ErrorType.Forbidden);
        }

        if (expectedRevision <= 0 || metadata.Revision != expectedRevision)
        {
            return Fail<HelloAuthorizationClientMutation>("Client has changed.", ErrorType.Conflict);
        }
        var oldType = descriptor.ClientType == OpenIddictConstants.ClientTypes.Confidential
            ? HelloAuthorizationClientType.Confidential
            : HelloAuthorizationClientType.Public;
        var secret = oldType != input.Type && input.Type == HelloAuthorizationClientType.Confidential ? NewSecret() : null;
        HelloClientRegistration.Apply(descriptor, ToOptions(input), new HelloClientMetadata(
            "administration", checked(metadata.Revision + 1), input.Resource,
            input.HonorReauthenticationPrompts, input.Scopes.ToArray()));
        if (oldType != input.Type)
        {
            descriptor.ClientSecret = secret;
        }
        try
        {
            await applications.UpdateAsync(app, descriptor, cancellationToken);
            return OperationResultFactory.Success(new HelloAuthorizationClientMutation(
                await ViewAsync(app, cancellationToken), secret));
        }
        catch (OpenIddictExceptions.ConcurrencyException)
        {
            return Fail<HelloAuthorizationClientMutation>("Client changed concurrently.", ErrorType.Conflict);
        }
        catch (OpenIddictExceptions.ValidationException)
        {
            return Fail<HelloAuthorizationClientMutation>("Client registration is invalid.");
        }
    }

    public async Task<OperationResult<bool>> DeleteAsync(
        string clientId, long expectedRevision, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return Fail<bool>("Invalid client ID.");
        }
        var app = await applications.FindByClientIdAsync(clientId, cancellationToken);
        if (app is null)
        {
            return Fail<bool>("Client was not found.", ErrorType.NotFound);
        }
        var metadata = HelloClientRegistration.Read(await HelloClientRegistration.DescribeAsync(applications, app, cancellationToken));
        if (metadata?.Ownership != "administration")
        {
            return Fail<bool>("Client is read-only.", ErrorType.Forbidden);
        }

        if (expectedRevision <= 0 || metadata.Revision != expectedRevision)
        {
            return Fail<bool>("Client has changed.", ErrorType.Conflict);
        }
        try
        {
            await applications.DeleteAsync(app, cancellationToken);
            return OperationResultFactory.Success(true);
        }
        catch (OpenIddictExceptions.ConcurrencyException)
        {
            return Fail<bool>("Client changed concurrently.", ErrorType.Conflict);
        }
    }

    public async Task<OperationResult<HelloAuthorizationClientMutation>> RotateSecretAsync(
        string clientId, long expectedRevision, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return Fail<HelloAuthorizationClientMutation>("Invalid client ID.");
        }
        var app = await applications.FindByClientIdAsync(clientId, cancellationToken);
        if (app is null)
        {
            return Fail<HelloAuthorizationClientMutation>("Client was not found.", ErrorType.NotFound);
        }
        var descriptor = await HelloClientRegistration.DescribeAsync(applications, app, cancellationToken);
        var metadata = HelloClientRegistration.Read(descriptor);
        if (metadata?.Ownership != "administration")
        {
            return Fail<HelloAuthorizationClientMutation>(
                "Client is read-only.", ErrorType.Forbidden);
        }

        if (expectedRevision <= 0 || metadata.Revision != expectedRevision)
        {
            return Fail<HelloAuthorizationClientMutation>(
                "Client has changed.", ErrorType.Conflict);
        }

        if (descriptor.ClientType != OpenIddictConstants.ClientTypes.Confidential)
        {
            return Fail<HelloAuthorizationClientMutation>(
                "A public client has no secret to rotate.");
        }
        var secret = NewSecret();
        descriptor.ClientSecret = secret;
        HelloClientRegistration.Write(descriptor, metadata with { Revision = checked(metadata.Revision + 1) });
        try
        {
            await applications.UpdateAsync(app, descriptor, cancellationToken);
            return OperationResultFactory.Success(new HelloAuthorizationClientMutation(
                await ViewAsync(app, cancellationToken), secret));
        }
        catch (OpenIddictExceptions.ConcurrencyException)
        {
            return Fail<HelloAuthorizationClientMutation>("Client changed concurrently.", ErrorType.Conflict);
        }
    }

    private async Task<HelloAuthorizationClientView> ViewAsync(object app, CancellationToken ct)
    {
        var d = await HelloClientRegistration.DescribeAsync(applications, app, ct);
        var metadata = HelloClientRegistration.Read(d);
        var scopes = metadata?.Scopes ?? d.Permissions
            .Where(x => x.StartsWith(
                OpenIddictConstants.Permissions.Prefixes.Scope,
                StringComparison.Ordinal))
            .Select(x => x[OpenIddictConstants.Permissions.Prefixes.Scope.Length..])
            .ToArray();
        return new HelloAuthorizationClientView(
            d.ClientId ?? string.Empty,
            d.DisplayName ?? string.Empty,
            d.ClientType == OpenIddictConstants.ClientTypes.Confidential
                ? HelloAuthorizationClientType.Confidential
                : HelloAuthorizationClientType.Public,
            metadata?.Resource ?? options.Resource,
            Array.AsReadOnly(d.RedirectUris.Select(x => x.OriginalString).ToArray()),
            Array.AsReadOnly(d.PostLogoutRedirectUris.Select(x => x.OriginalString).ToArray()),
            Array.AsReadOnly(scopes),
            metadata?.HonorReauthenticationPrompts ?? true,
            metadata?.Ownership ?? "legacy",
            metadata?.Revision ?? 0);
    }

    private string? Validate(HelloAuthorizationClientInput input)
    {
        if (input is null || input.RedirectUris is null
            || input.PostLogoutRedirectUris is null || input.Scopes is null
            || !options.GetResources().Contains(input.Resource, StringComparer.Ordinal))
        {
            return "Invalid client or resource.";
        }
        try
        {
            options.ValidateManagedClient(ToOptions(input));
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return ex.Message;
        }
    }

    private static HelloAuthorizationClientOptions ToOptions(HelloAuthorizationClientInput input)
        => new()
        {
            ClientId = input.ClientId,
            DisplayName = input.DisplayName,
            Type = input.Type,
            ClientSecret = input.Type == HelloAuthorizationClientType.Confidential
                ? "validation-only" : null,
            Resource = input.Resource,
            RedirectUris = input.RedirectUris.ToList(),
            PostLogoutRedirectUris = input.PostLogoutRedirectUris.ToList(),
            Scopes = input.Scopes.ToList(),
            HonorReauthenticationPrompts = input.HonorReauthenticationPrompts,
        };

    private static bool IsUniqueViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is System.Data.Common.DbException { SqlState: "23505" })
            {
                return true;
            }

            // SQLite extended constraint codes 1555 and 2067 identify primary-key
            // and unique violations, respectively. Do not hide other database errors.
            if (current.GetType().FullName == "Microsoft.Data.Sqlite.SqliteException"
                && current.GetType().GetProperty("SqliteExtendedErrorCode")
                    ?.GetValue(current) is int code
                && code is 1555 or 2067)
            {
                return true;
            }
        }
        return false;
    }

    private static string NewSecret() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private static OperationResult<T> Fail<T>(string message, ErrorType type = ErrorType.Validation)
        => OperationResultFactory.Fail<T>(new Error("authorization_client_invalid", message, type));
}
