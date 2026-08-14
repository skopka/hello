namespace Skopka.Hello;

public sealed class HelloDeliveryOptions
{
    public int AnonymousRequestQueueCapacity { get; set; } = 256;

    public HelloDeliveryChannel VerificationChannel { get; set; } =
        HelloDeliveryChannel.Email;

    /// <summary>
    /// Requires the RFC 6238 authenticator method for sensitive actions when
    /// the acting user has enabled it. Users without an authenticator keep
    /// using the configured confirmed-contact channel.
    /// </summary>
    public bool RequireTotpWhenEnabled { get; set; }

    public string? EmailProviderId { get; set; }

    public string? SmsProviderId { get; set; }

    internal void Validate()
    {
        if (VerificationChannel is not HelloDeliveryChannel.Email
            and not HelloDeliveryChannel.Sms)
        {
            throw new InvalidOperationException(
                "The fallback verification channel must be Email or Sms.");
        }

        if (AnonymousRequestQueueCapacity is < 1 or > 10_000)
        {
            throw new InvalidOperationException(
                "The anonymous account-message queue capacity must be between 1 and 10000.");
        }
    }

    internal string? GetProviderId(HelloDeliveryChannel channel)
        => channel switch
        {
            HelloDeliveryChannel.Email => EmailProviderId,
            HelloDeliveryChannel.Sms => SmsProviderId,
            HelloDeliveryChannel.Authenticator => null,
            _ => throw new ArgumentOutOfRangeException(
                nameof(channel),
                channel,
                "The delivery channel is unsupported."),
        };
}
