using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

internal sealed class DirectSmtpHelloAccountMessageProvider(
    HelloSmtpOptions options,
    SmtpHelloAccountMessageTransport transport)
    : IHelloAccountMessageProvider
{
    public string ProviderId => options.ProviderId;

    public HelloDeliveryChannel Channel => HelloDeliveryChannel.Email;

    public Task<OperationResult> SendAsync(
        HelloAccountMessage message,
        CancellationToken cancellationToken)
        => transport.SendAsync(message, cancellationToken);
}
