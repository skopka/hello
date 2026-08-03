using System.Diagnostics.Metrics;

namespace Skopka.Hello;

public static class HelloDiagnostics
{
    public const string MeterName = "Skopka.Hello";

    internal static readonly Meter Meter = new(MeterName);

    internal static readonly Counter<long> AnonymousQueueDrops =
        Meter.CreateCounter<long>(
            "skopka.hello.account_message.queue.dropped",
            "{message}",
            "Anonymous account-message requests dropped because the in-memory queue was full.");
}
