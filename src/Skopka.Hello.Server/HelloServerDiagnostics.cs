using System.Diagnostics.Metrics;

namespace Skopka.Hello.Server;

internal static class HelloServerDiagnostics
{
    public const string MeterName = "Skopka.Hello.Server";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> AnonymousRequestsPersisted =
        Meter.CreateCounter<long>(
            "skopka.hello.delivery.anonymous.persisted",
            "{request}");

    public static readonly Counter<long> AnonymousRequestsCompleted =
        Meter.CreateCounter<long>(
            "skopka.hello.delivery.anonymous.completed",
            "{request}");

    public static readonly Counter<long> AccountMessagesPersisted =
        Meter.CreateCounter<long>(
            "skopka.hello.delivery.message.persisted",
            "{message}");

    public static readonly Counter<long> AccountMessagesDelivered =
        Meter.CreateCounter<long>(
            "skopka.hello.delivery.message.delivered",
            "{message}");

    public static readonly Counter<long> DeliveryRetries =
        Meter.CreateCounter<long>(
            "skopka.hello.delivery.retry",
            "{attempt}");

    public static readonly Counter<long> DeadLetters =
        Meter.CreateCounter<long>(
            "skopka.hello.delivery.dead_letter",
            "{message}");

    public static readonly Counter<long> AuditRecordsPersisted =
        Meter.CreateCounter<long>(
            "skopka.hello.audit.persisted",
            "{record}");

    public static readonly Counter<long> PersistenceFailures =
        Meter.CreateCounter<long>(
            "skopka.hello.persistence.failure",
            "{failure}");

    public static readonly Counter<long> RecordsPruned =
        Meter.CreateCounter<long>(
            "skopka.hello.persistence.pruned",
            "{record}");
}
