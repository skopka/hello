namespace Skopka.Hello;

public sealed class HelloDeliveryOptions
{
    public int AnonymousRequestQueueCapacity { get; set; } = 256;

    public HelloDeliveryChannel VerificationChannel { get; set; } =
        HelloDeliveryChannel.Email;

    public string? EmailProviderId { get; set; }

    public string? SmsProviderId { get; set; }

    internal void Validate()
    {
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
            _ => throw new ArgumentOutOfRangeException(
                nameof(channel),
                channel,
                "The delivery channel is unsupported."),
        };
}
