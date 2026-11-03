using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.AuthorizationServer;

public sealed record HelloAuthorizationClientInput(
    string ClientId,
    string DisplayName,
    HelloAuthorizationClientType Type,
    string Resource,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris,
    IReadOnlyList<string> Scopes,
    bool HonorReauthenticationPrompts);

public sealed record HelloAuthorizationClientView(
    string ClientId,
    string DisplayName,
    HelloAuthorizationClientType Type,
    string Resource,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris,
    IReadOnlyList<string> Scopes,
    bool HonorReauthenticationPrompts,
    string Ownership,
    long Revision);

/// <summary>
/// A newly generated secret is returned only in the successful create, type
/// transition or rotation result. It cannot be retrieved again.
/// </summary>
public sealed record HelloAuthorizationClientMutation(
    HelloAuthorizationClientView Client,
    string? Secret)
{
    public override string ToString()
        => $"{nameof(HelloAuthorizationClientMutation)} {{ Client = {Client}, Secret = [redacted] }}";
}

public sealed record HelloAuthorizationClientCatalog(
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> Resources);

/// <summary>
/// Manages registered first-party clients. Callers must enforce authorization
/// and any required step-up before invoking mutation methods.
/// </summary>
public interface IHelloAuthorizationClientManagement
{
    HelloAuthorizationClientCatalog GetCatalog();

    Task<OperationResult<IReadOnlyList<HelloAuthorizationClientView>>> ListAsync(
        int offset, int count, CancellationToken cancellationToken);

    Task<OperationResult<HelloAuthorizationClientView>> GetAsync(
        string clientId, CancellationToken cancellationToken);

    Task<OperationResult<bool>> ValidateCreateAsync(
        HelloAuthorizationClientInput input, CancellationToken cancellationToken);

    Task<OperationResult<bool>> ValidateUpdateAsync(
        string clientId, long expectedRevision, HelloAuthorizationClientInput input,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloAuthorizationClientMutation>> CreateAsync(
        HelloAuthorizationClientInput input, CancellationToken cancellationToken);

    Task<OperationResult<HelloAuthorizationClientMutation>> UpdateAsync(
        string clientId, long expectedRevision, HelloAuthorizationClientInput input,
        CancellationToken cancellationToken);

    Task<OperationResult<bool>> DeleteAsync(
        string clientId, long expectedRevision, CancellationToken cancellationToken);

    Task<OperationResult<HelloAuthorizationClientMutation>> RotateSecretAsync(
        string clientId, long expectedRevision, CancellationToken cancellationToken);
}
