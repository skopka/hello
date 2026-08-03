using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

internal sealed class QueuedSmtpHelloAccountMessageProvider(
    HelloSmtpOptions options,
    SmtpHelloAccountMessageQueue queue)
    : IHelloAccountMessageProvider
{
    public string ProviderId => options.ProviderId;

    public HelloDeliveryChannel Channel => HelloDeliveryChannel.Email;

    public Task<OperationResult> SendAsync(
        HelloAccountMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        if (message.Channel != HelloDeliveryChannel.Email)
        {
            return Task.FromResult(
                OperationResultFactory.Fail(
                    HelloAccountMessageValidator.ChannelMismatch()));
        }

        var validation = HelloAccountMessageValidator.Validate(
            message,
            DateTimeOffset.UtcNow);
        if (validation is not null)
        {
            return Task.FromResult(
                OperationResultFactory.Fail(validation));
        }

        var result = queue.TryWrite(message)
            ? OperationResultFactory.Success()
            : OperationResultFactory.Fail(
                new Error(
                    HelloDeliveryErrorCodes.QueueFull,
                    "The account message queue is full.",
                    ErrorType.Failure));
        return Task.FromResult(result);
    }
}
