using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Skopka.Hello.Server;

internal sealed partial class HelloServerPersistencePruningWorker(
    NpgsqlDataSource dataSource,
    HelloServerPersistenceOptions options,
    ILogger<HelloServerPersistencePruningWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PruneAsync(stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (NpgsqlException)
            {
                PruningFailed(logger, null);
                HelloServerDiagnostics.PersistenceFailures.Add(
                    1,
                    new KeyValuePair<string, object?>(
                        "operation",
                        "prune"));
            }

            await Task.Delay(options.PruningInterval, stoppingToken);
        }
    }

    private async Task PruneAsync(CancellationToken cancellationToken)
    {
        await MarkExpiredAsync(
            "skopka_hello.anonymous_account_message_inbox",
            cancellationToken);
        await MarkExpiredAsync(
            "skopka_hello.account_message_outbox",
            cancellationToken);
        await DeleteFailedAsync(
            "skopka_hello.anonymous_account_message_inbox",
            "anonymous",
            cancellationToken);
        await DeleteFailedAsync(
            "skopka_hello.account_message_outbox",
            "message",
            cancellationToken);
        await DeleteAuditAsync(cancellationToken);
    }

    private async Task MarkExpiredAsync(
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            WITH candidates AS
            (
                SELECT id
                FROM {tableName}
                WHERE failed_at IS NULL
                  AND expires_at <= CURRENT_TIMESTAMP
                ORDER BY expires_at, id
                LIMIT $1
                FOR UPDATE SKIP LOCKED
            )
            UPDATE {tableName} AS records
            SET failed_at = CURRENT_TIMESTAMP,
                last_error_code = $2,
                lease_id = NULL,
                leased_until = NULL
            FROM candidates
            WHERE records.id = candidates.id;
            """);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Integer,
            options.PruningBatchSize);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Varchar,
            HelloDeliveryErrorCodes.Expired);
        var affected = await command.ExecuteNonQueryAsync(
            cancellationToken);
        if (affected > 0)
        {
            HelloServerDiagnostics.DeadLetters.Add(
                affected,
                new KeyValuePair<string, object?>(
                    "stage",
                    "expired"));
        }
    }

    private async Task DeleteFailedAsync(
        string tableName,
        string recordType,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            WITH candidates AS
            (
                SELECT id
                FROM {tableName}
                WHERE failed_at < CURRENT_TIMESTAMP - $1
                ORDER BY failed_at, id
                LIMIT $2
            )
            DELETE FROM {tableName} AS records
            USING candidates
            WHERE records.id = candidates.id;
            """);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Interval,
            options.FailedRecordRetention);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Integer,
            options.PruningBatchSize);
        var affected = await command.ExecuteNonQueryAsync(
            cancellationToken);
        RecordPruned(recordType, affected);
    }

    private async Task DeleteAuditAsync(
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            WITH candidates AS
            (
                SELECT id
                FROM skopka_hello.audit_outbox
                WHERE created_at < CURRENT_TIMESTAMP - $1
                ORDER BY created_at, id
                LIMIT $2
            )
            DELETE FROM skopka_hello.audit_outbox AS records
            USING candidates
            WHERE records.id = candidates.id;
            """);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Interval,
            options.AuditRetention);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Integer,
            options.PruningBatchSize);
        var affected = await command.ExecuteNonQueryAsync(
            cancellationToken);
        RecordPruned("audit", affected);
    }

    private static void RecordPruned(string recordType, int affected)
    {
        if (affected <= 0)
        {
            return;
        }

        HelloServerDiagnostics.RecordsPruned.Add(
            affected,
            new KeyValuePair<string, object?>(
                "record.type",
                recordType));
    }

    [LoggerMessage(
        EventId = 2131,
        Level = LogLevel.Error,
        Message = "Hello persistence pruning failed.")]
    private static partial void PruningFailed(
        ILogger logger,
        Exception? exception);
}
