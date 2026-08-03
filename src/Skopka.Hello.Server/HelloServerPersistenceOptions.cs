namespace Skopka.Hello.Server;

internal sealed class HelloServerPersistenceOptions
{
    public bool DurableDeliveryEnabled { get; set; } = true;

    public bool AuditEnabled { get; set; } = true;

    public TimeSpan PollingInterval { get; set; } =
        TimeSpan.FromSeconds(1);

    public TimeSpan CommandTimeout { get; set; } =
        TimeSpan.FromSeconds(5);

    public TimeSpan LeaseDuration { get; set; } =
        TimeSpan.FromMinutes(1);

    public TimeSpan RetryDelay { get; set; } =
        TimeSpan.FromSeconds(10);

    public int MaximumAttempts { get; set; } = 8;

    public TimeSpan AnonymousRequestLifetime { get; set; } =
        TimeSpan.FromHours(1);

    public TimeSpan FailedRecordRetention { get; set; } =
        TimeSpan.FromDays(7);

    public TimeSpan AuditRetention { get; set; } =
        TimeSpan.FromDays(90);

    public TimeSpan PruningInterval { get; set; } =
        TimeSpan.FromHours(1);

    public int PruningBatchSize { get; set; } = 500;

    public void Validate()
    {
        if (PollingInterval < TimeSpan.Zero
            || PollingInterval > TimeSpan.FromMinutes(1))
        {
            throw new InvalidOperationException(
                "The persistence polling interval must be between zero and one minute.");
        }

        if (CommandTimeout < TimeSpan.FromSeconds(1)
            || CommandTimeout > TimeSpan.FromMinutes(1))
        {
            throw new InvalidOperationException(
                "The persistence command timeout must be between one second and one minute.");
        }

        if (LeaseDuration < TimeSpan.FromSeconds(10)
            || LeaseDuration > TimeSpan.FromMinutes(30))
        {
            throw new InvalidOperationException(
                "The persistence lease duration must be between ten seconds and thirty minutes.");
        }

        if (RetryDelay < TimeSpan.FromSeconds(1)
            || RetryDelay > TimeSpan.FromHours(1))
        {
            throw new InvalidOperationException(
                "The persistence retry delay must be between one second and one hour.");
        }

        if (MaximumAttempts is < 1 or > 100)
        {
            throw new InvalidOperationException(
                "The persistence maximum attempt count must be between 1 and 100.");
        }

        if (AnonymousRequestLifetime < TimeSpan.FromMinutes(1)
            || AnonymousRequestLifetime > TimeSpan.FromDays(1))
        {
            throw new InvalidOperationException(
                "The anonymous request lifetime must be between one minute and one day.");
        }

        if (FailedRecordRetention < TimeSpan.FromHours(1)
            || FailedRecordRetention > TimeSpan.FromDays(90))
        {
            throw new InvalidOperationException(
                "The failed delivery retention must be between one hour and ninety days.");
        }

        if (AuditRetention < TimeSpan.FromDays(1)
            || AuditRetention > TimeSpan.FromDays(3650))
        {
            throw new InvalidOperationException(
                "The audit retention must be between one day and ten years.");
        }

        if (PruningInterval < TimeSpan.FromMinutes(1)
            || PruningInterval > TimeSpan.FromDays(1))
        {
            throw new InvalidOperationException(
                "The persistence pruning interval must be between one minute and one day.");
        }

        if (PruningBatchSize is < 1 or > 10_000)
        {
            throw new InvalidOperationException(
                "The persistence pruning batch size must be between 1 and 10000.");
        }
    }
}
