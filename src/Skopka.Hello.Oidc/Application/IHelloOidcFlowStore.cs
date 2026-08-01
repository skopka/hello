namespace Skopka.Hello.Oidc;

public interface IHelloOidcFlowStore
{
    Task<bool> TryConsumeAsync(
        Guid flowId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);
}
