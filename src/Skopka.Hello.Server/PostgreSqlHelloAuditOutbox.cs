using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.Server;

internal sealed class PostgreSqlHelloAuditOutbox(
    NpgsqlDataSource dataSource)
    : IHelloAuditOutbox, IHelloSecurityEventSink
{
    public OperationResult Write(
        HelloSecurityEventEnvelope securityEvent)
    {
        ArgumentNullException.ThrowIfNull(securityEvent);
        var record = new HelloAuditOutboxRecord(
            securityEvent.EventId,
            securityEvent.EventType,
            securityEvent.SubjectUserId,
            securityEvent.ActorUserId,
            securityEvent.ResourceId,
            securityEvent.CorrelationId,
            securityEvent.OccurredAt,
            DateTimeOffset.UtcNow,
            securityEvent.Metadata);
        var validation = Validate(record);
        if (validation is not null)
        {
            return OperationResultFactory.Fail(validation);
        }

        try
        {
            using var command = CreateCommand(record);
            var affected = command.ExecuteNonQuery();
            if (affected > 0)
            {
                HelloServerDiagnostics.AuditRecordsPersisted.Add(1);
            }

            return OperationResultFactory.Success();
        }
        catch (NpgsqlException)
        {
            HelloServerDiagnostics.PersistenceFailures.Add(
                1,
                new KeyValuePair<string, object?>(
                    "operation",
                    "audit.write"));
            return Failed();
        }
        catch (JsonException)
        {
            return InvalidRecord();
        }
    }

    public async Task<OperationResult> AddAsync(
        HelloAuditOutboxRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        var validation = Validate(record);
        if (validation is not null)
        {
            return OperationResultFactory.Fail(validation);
        }

        try
        {
            await using var command = CreateCommand(record);
            var affected = await command.ExecuteNonQueryAsync(
                cancellationToken);
            if (affected > 0)
            {
                HelloServerDiagnostics.AuditRecordsPersisted.Add(1);
            }

            return OperationResultFactory.Success();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (NpgsqlException)
        {
            HelloServerDiagnostics.PersistenceFailures.Add(
                1,
                new KeyValuePair<string, object?>(
                    "operation",
                    "audit.add"));
            return Failed();
        }
        catch (JsonException)
        {
            return InvalidRecord();
        }
    }

    private NpgsqlCommand CreateCommand(HelloAuditOutboxRecord record)
    {
        var command = dataSource.CreateCommand(
            """
            INSERT INTO skopka_hello.audit_outbox
                (id, event_type, subject_user_id, actor_user_id,
                 resource_id, correlation_id, occurred_at, created_at,
                 metadata)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
            ON CONFLICT (id) DO NOTHING;
            """);
        command.Parameters.AddWithValue(NpgsqlDbType.Uuid, record.Id);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Varchar,
            record.EventType);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Uuid,
            record.SubjectUserId is { } subjectUserId
                ? subjectUserId
                : DBNull.Value);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Uuid,
            record.ActorUserId is { } actorUserId
                ? actorUserId
                : DBNull.Value);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Uuid,
            record.ResourceId is { } resourceId
                ? resourceId
                : DBNull.Value);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Varchar,
            record.CorrelationId is { } correlationId
                ? correlationId
                : DBNull.Value);
        command.Parameters.AddWithValue(
            NpgsqlDbType.TimestampTz,
            record.OccurredAt);
        command.Parameters.AddWithValue(
            NpgsqlDbType.TimestampTz,
            record.CreatedAt);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(record.Metadata));
        return command;
    }

    private static Error? Validate(HelloAuditOutboxRecord record)
    {
        if (record.Id == Guid.Empty
            || string.IsNullOrWhiteSpace(record.EventType)
            || record.EventType.Length > 128
            || record.OccurredAt == default
            || record.CreatedAt == default
            || record.CorrelationId?.Length > 128
            || record.Metadata.Count > 32)
        {
            return InvalidRecordError();
        }

        foreach (var pair in record.Metadata)
        {
            if (string.IsNullOrWhiteSpace(pair.Key)
                || pair.Key.Length > 128
                || pair.Value is null
                || pair.Value.Length > 512)
            {
                return InvalidRecordError();
            }
        }

        return null;
    }

    private static OperationResult InvalidRecord()
        => OperationResultFactory.Fail(InvalidRecordError());

    private static Error InvalidRecordError()
        => new(
            HelloAuditErrorCodes.InvalidRecord,
            "The audit outbox record is invalid.",
            ErrorType.Failure);

    private static OperationResult Failed()
        => OperationResultFactory.Fail(
            new Error(
                HelloAuditErrorCodes.Failed,
                "The audit outbox write failed.",
                ErrorType.Failure));
}
