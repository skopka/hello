using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

internal abstract class HelloChannelProviderRegistration<TProvider>(
    TProvider provider,
    HelloDeliveryChannel expectedChannel)
    : IHelloAccountMessageProvider
    where TProvider : class, IHelloAccountMessageProvider
{
    private readonly TProvider provider = Validate(
        provider,
        expectedChannel);

    public string ProviderId => provider.ProviderId;

    public HelloDeliveryChannel Channel => expectedChannel;

    public Task<OperationResult> SendAsync(
        HelloAccountMessage message,
        CancellationToken cancellationToken)
        => provider.SendAsync(message, cancellationToken);

    private static TProvider Validate(
        TProvider provider,
        HelloDeliveryChannel expectedChannel)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (provider.Channel != expectedChannel)
        {
            throw new InvalidOperationException(
                $"Delivery provider '{provider.ProviderId}' was registered for {expectedChannel} but reports {provider.Channel}.");
        }

        return provider;
    }
}

internal sealed class HelloEmailProviderRegistration<TProvider>(
    TProvider provider)
    : HelloChannelProviderRegistration<TProvider>(
        provider,
        HelloDeliveryChannel.Email)
    where TProvider : class, IHelloAccountMessageProvider;

internal sealed class HelloSmsProviderRegistration<TProvider>(
    TProvider provider)
    : HelloChannelProviderRegistration<TProvider>(
        provider,
        HelloDeliveryChannel.Sms)
    where TProvider : class, IHelloAccountMessageProvider;
