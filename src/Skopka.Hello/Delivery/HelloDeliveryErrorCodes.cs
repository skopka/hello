namespace Skopka.Hello;

public static class HelloDeliveryErrorCodes
{
    public const string NotConfigured =
        "hello.delivery.not_configured";

    public const string InvalidMessage =
        "hello.delivery.invalid_message";

    public const string Expired =
        "hello.delivery.expired";

    public const string ChannelMismatch =
        "hello.delivery.channel_mismatch";

    public const string Failed =
        "hello.delivery.failed";

    public const string QueueFull =
        "hello.delivery.queue_full";
}
