namespace Skopka.Hello.AuthorizationServer;

public interface IHelloAuthorizationClientSynchronizer
{
    Task SynchronizeAsync(CancellationToken cancellationToken);
}
