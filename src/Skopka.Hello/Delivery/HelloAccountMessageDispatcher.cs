using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello;

internal sealed class HelloAccountMessageDispatcher
    : IHelloAccountMessageSender
{
    private readonly Dictionary<
        HelloDeliveryChannel,
        IHelloAccountMessageProvider> routes;

    public HelloAccountMessageDispatcher(
        HelloDeliveryOptions options,
        IEnumerable<IHelloAccountMessageProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(providers);

        if (options.VerificationChannel is not HelloDeliveryChannel.Email
            and not HelloDeliveryChannel.Sms)
        {
            throw new InvalidOperationException(
                "The verification delivery channel is unsupported.");
        }

        var byId = new Dictionary<
            string,
            IHelloAccountMessageProvider>(
                StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            var providerId = NormalizeProviderId(
                provider.ProviderId,
                "A delivery provider id");
            if (provider.Channel is not HelloDeliveryChannel.Email
                and not HelloDeliveryChannel.Sms)
            {
                throw new InvalidOperationException(
                    $"Delivery provider '{providerId}' reports an unsupported channel.");
            }

            if (!byId.TryAdd(providerId, provider))
            {
                throw new InvalidOperationException(
                    $"Delivery provider id '{providerId}' is registered more than once.");
            }
        }

        var configuredRoutes = new Dictionary<
            HelloDeliveryChannel,
            IHelloAccountMessageProvider>();
        AddRoute(
            HelloDeliveryChannel.Email,
            options.GetProviderId(HelloDeliveryChannel.Email),
            byId,
            configuredRoutes);
        AddRoute(
            HelloDeliveryChannel.Sms,
            options.GetProviderId(HelloDeliveryChannel.Sms),
            byId,
            configuredRoutes);
        routes = configuredRoutes;
    }

    public async Task<OperationResult> SendAsync(
        HelloAccountMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        var validation = HelloAccountMessageValidator.Validate(
            message,
            DateTimeOffset.UtcNow);
        if (validation is not null)
        {
            return OperationResultFactory.Fail(validation);
        }

        if (!routes.TryGetValue(message.Channel, out var provider))
        {
            return OperationResultFactory.Fail(
                new Error(
                    HelloDeliveryErrorCodes.NotConfigured,
                    "Account message delivery is not configured for this channel.",
                    ErrorType.Failure));
        }

        try
        {
            return await provider.SendAsync(
                message,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return ProviderFailed();
        }
        catch (Exception)
        {
            // Providers are an application extension boundary. Convert
            // faults to the shared result contract without exposing provider
            // details or turning known-recipient requests into HTTP 500s.
            return ProviderFailed();
        }
    }

    public OperationResult CheckAvailability(
        HelloDeliveryChannel channel)
        => routes.ContainsKey(channel)
            ? OperationResultFactory.Success()
            : OperationResultFactory.Fail(
                new Error(
                    HelloDeliveryErrorCodes.NotConfigured,
                    "Account message delivery is not configured for this channel.",
                    ErrorType.Failure));

    internal static string NormalizeProviderId(
        string? value,
        string parameterName)
    {
        var providerId = value?.Trim();
        if (string.IsNullOrWhiteSpace(providerId)
            || providerId.Length > 64
            || providerId.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_' and not '.'))
        {
            throw new InvalidOperationException(
                $"{parameterName} must contain 1 to 64 ASCII letters, digits, '.', '_' or '-'.");
        }

        return providerId;
    }

    private static void AddRoute(
        HelloDeliveryChannel channel,
        string? configuredProviderId,
        Dictionary<string, IHelloAccountMessageProvider> providers,
        Dictionary<HelloDeliveryChannel, IHelloAccountMessageProvider> routes)
    {
        if (string.IsNullOrWhiteSpace(configuredProviderId))
        {
            return;
        }

        var providerId = NormalizeProviderId(
            configuredProviderId,
            $"The configured {channel} provider id");
        if (!providers.TryGetValue(providerId, out var provider))
        {
            throw new InvalidOperationException(
                $"Delivery provider '{providerId}' configured for {channel} is not registered.");
        }

        if (provider.Channel != channel)
        {
            throw new InvalidOperationException(
                $"Delivery provider '{providerId}' uses channel {provider.Channel} and cannot be configured for {channel}.");
        }

        routes.Add(channel, provider);
    }

    private static OperationResult ProviderFailed()
        => OperationResultFactory.Fail(
            new Error(
                HelloDeliveryErrorCodes.Failed,
                "The account message provider failed.",
                ErrorType.Failure));
}
