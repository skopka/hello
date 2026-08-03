using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;

namespace Skopka.Hello.Server;

internal static class HelloServerDatabaseMigrator
{
    private const long AdvisoryLockId = 6008745234780322892;
    private const string SchemaName = "skopka_hello";
    private const string HistoryTableName = "schema_migrations";

    private static readonly DatabaseMigration[] Migrations =
    [
        new(
            "202608040001_durable_delivery_and_audit",
            """
            CREATE TABLE skopka_hello.anonymous_account_message_inbox
            (
                id uuid PRIMARY KEY,
                kind smallint NOT NULL,
                protected_payload bytea NOT NULL,
                created_at timestamp with time zone NOT NULL,
                available_at timestamp with time zone NOT NULL,
                expires_at timestamp with time zone NOT NULL,
                attempts integer NOT NULL DEFAULT 0,
                lease_id uuid NULL,
                leased_until timestamp with time zone NULL,
                failed_at timestamp with time zone NULL,
                last_error_code varchar(128) NULL
            );

            CREATE INDEX ix_hello_anonymous_inbox_available
                ON skopka_hello.anonymous_account_message_inbox
                    (available_at, created_at, id)
                WHERE failed_at IS NULL;

            CREATE TABLE skopka_hello.account_message_outbox
            (
                id uuid PRIMARY KEY,
                kind smallint NOT NULL,
                channel smallint NOT NULL,
                route_provider_id varchar(64) NOT NULL,
                protected_payload bytea NOT NULL,
                created_at timestamp with time zone NOT NULL,
                available_at timestamp with time zone NOT NULL,
                expires_at timestamp with time zone NOT NULL,
                attempts integer NOT NULL DEFAULT 0,
                lease_id uuid NULL,
                leased_until timestamp with time zone NULL,
                failed_at timestamp with time zone NULL,
                last_error_code varchar(128) NULL
            );

            CREATE INDEX ix_hello_account_outbox_available
                ON skopka_hello.account_message_outbox
                    (available_at, created_at, id)
                WHERE failed_at IS NULL;

            CREATE TABLE skopka_hello.audit_outbox
            (
                id uuid PRIMARY KEY,
                event_type varchar(128) NOT NULL,
                subject_user_id uuid NULL,
                actor_user_id uuid NULL,
                resource_id uuid NULL,
                correlation_id varchar(128) NULL,
                occurred_at timestamp with time zone NOT NULL,
                created_at timestamp with time zone NOT NULL,
                metadata jsonb NOT NULL,
                published_at timestamp with time zone NULL
            );

            CREATE INDEX ix_hello_audit_outbox_created
                ON skopka_hello.audit_outbox (created_at, id);

            CREATE INDEX ix_hello_audit_outbox_unpublished
                ON skopka_hello.audit_outbox (created_at, id)
                WHERE published_at IS NULL;
            """),
    ];

    public static string LatestMigrationId => Migrations[^1].Id;

    public static async Task<int> ApplyAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        await using var dataSource = NpgsqlDataSource.Create(
            connectionString);
        await using var connection = await dataSource.OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);

        await ExecuteAsync(
            connection,
            transaction,
            $"SELECT pg_advisory_xact_lock({AdvisoryLockId});",
            cancellationToken);
        await ExecuteAsync(
            connection,
            transaction,
            $"""
            CREATE SCHEMA IF NOT EXISTS {SchemaName};
            CREATE TABLE IF NOT EXISTS {SchemaName}.{HistoryTableName}
            (
                migration_id varchar(128) PRIMARY KEY,
                checksum char(64) NOT NULL,
                applied_at timestamp with time zone NOT NULL
            );
            """,
            cancellationToken);

        var applied = await ReadAppliedAsync(
            connection,
            transaction,
            cancellationToken);
        var appliedCount = 0;
        foreach (var migration in Migrations)
        {
            var checksum = ComputeChecksum(migration.Sql);
            if (applied.TryGetValue(migration.Id, out var storedChecksum))
            {
                if (!string.Equals(
                    storedChecksum,
                    checksum,
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Hello database migration '{migration.Id}' has a checksum mismatch.");
                }

                continue;
            }

            await ExecuteAsync(
                connection,
                transaction,
                migration.Sql,
                cancellationToken);
            await InsertAppliedAsync(
                connection,
                transaction,
                migration.Id,
                checksum,
                cancellationToken);
            appliedCount++;
        }

        await transaction.CommitAsync(cancellationToken);
        return appliedCount;
    }

    public static async Task<bool> IsCurrentAsync(
        NpgsqlDataSource dataSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        try
        {
            await using var command = dataSource.CreateCommand(
                $"""
                SELECT checksum
                FROM {SchemaName}.{HistoryTableName}
                WHERE migration_id = $1;
                """);
            command.Parameters.AddWithValue(
                NpgsqlDbType.Varchar,
                LatestMigrationId);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is string checksum
                && string.Equals(
                    checksum,
                    ComputeChecksum(Migrations[^1].Sql),
                    StringComparison.Ordinal);
        }
        catch (PostgresException exception)
            when (exception.SqlState is "42P01" or "3F000")
        {
            return false;
        }
    }

    private static async Task<Dictionary<string, string>> ReadAppliedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            SELECT migration_id, checksum
            FROM {SchemaName}.{HistoryTableName};
            """,
            connection,
            transaction);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        var applied = new Dictionary<string, string>(
            StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            applied.Add(
                reader.GetString(0),
                reader.GetString(1));
        }

        return applied;
    }

    private static async Task InsertAppliedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string migrationId,
        string checksum,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            INSERT INTO {SchemaName}.{HistoryTableName}
                (migration_id, checksum, applied_at)
            VALUES ($1, $2, CURRENT_TIMESTAMP);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Varchar,
            migrationId);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Char,
            checksum);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            sql,
            connection,
            transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ComputeChecksum(string sql)
        => Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(sql)))
            .ToLowerInvariant();

    private sealed record DatabaseMigration(string Id, string Sql);
}
