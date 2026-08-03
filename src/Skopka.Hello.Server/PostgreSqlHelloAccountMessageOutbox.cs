using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.Server;

internal sealed record HelloDurableEmailRouteOptions(
    string DestinationProviderId);

internal sealed record HelloAccountMessageOutboxLease(
    Guid LeaseId,
    string DestinationProviderId,
    HelloAccountMessage Message);

internal sealed partial class PostgreSqlHelloAccountMessageOutbox(
    NpgsqlDataSource dataSource,
    HelloProtectedPayloadSerializer serializer,
    HelloServerPersistenceOptions options,
    HelloDurableEmailRouteOptions route,
    ILogger<PostgreSqlHelloAccountMessageOutbox> logger)
    : IHelloAccountMessageProvider
{
    public const string DurableEmailProviderId =
        "postgres-outbox-email";

    public string ProviderId => DurableEmailProviderId;

    public HelloDeliveryChannel Channel => HelloDeliveryChannel.Email;

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

        if (message.Channel != HelloDeliveryChannel.Email)
        {
            return OperationResultFactory.Fail(
                HelloAccountMessageValidator.ChannelMismatch());
        }

        byte[] payload;
        try
        {
            payload = serializer.ProtectAccountMessage(message);
        }
        catch (CryptographicException)
        {
            return Failed();
        }

        try
        {
            await using var command = dataSource.CreateCommand(
                """
                INSERT INTO skopka_hello.account_message_outbox
                    (id, kind, channel, route_provider_id,
                     protected_payload, created_at, available_at, expires_at)
                VALUES ($1, $2, $3, $4, $5,
                        CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, $6)
                ON CONFLICT (id) DO NOTHING;
                """);
            command.Parameters.AddWithValue(
                NpgsqlDbType.Uuid,
                message.MessageId);
            command.Parameters.AddWithValue(
                NpgsqlDbType.Smallint,
                (short)message.Kind);
            command.Parameters.AddWithValue(
                NpgsqlDbType.Smallint,
                (short)message.Channel);
            command.Parameters.AddWithValue(
                NpgsqlDbType.Varchar,
                route.DestinationProviderId);
            command.Parameters.AddWithValue(
                NpgsqlDbType.Bytea,
                payload);
            command.Parameters.AddWithValue(
                NpgsqlDbType.TimestampTz,
                message.ExpiresAt);
            var affected = await command.ExecuteNonQueryAsync(
                cancellationToken);
            if (affected > 0)
            {
                HelloServerDiagnostics.AccountMessagesPersisted.Add(
                    1,
                    new KeyValuePair<string, object?>(
                        "message.kind",
                        message.Kind.ToString()));
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
            OutboxWriteFailed(
                logger,
                message.MessageId,
                message.Kind,
                null);
            HelloServerDiagnostics.PersistenceFailures.Add(
                1,
                new KeyValuePair<string, object?>(
                    "operation",
                    "message.enqueue"));
            return Failed();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public async Task<HelloAccountMessageOutboxLease?> TryLeaseAsync(
        CancellationToken cancellationToken)
    {
        var leaseId = Guid.NewGuid();
        await using var command = dataSource.CreateCommand(
            """
            WITH candidate AS
            (
                SELECT id
                FROM skopka_hello.account_message_outbox
                WHERE failed_at IS NULL
                  AND expires_at > CURRENT_TIMESTAMP
                  AND available_at <= CURRENT_TIMESTAMP
                  AND (leased_until IS NULL OR leased_until < CURRENT_TIMESTAMP)
                ORDER BY created_at, id
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            UPDATE skopka_hello.account_message_outbox AS outbox
            SET lease_id = $1,
                leased_until = CURRENT_TIMESTAMP + $2,
                attempts = attempts + 1
            FROM candidate
            WHERE outbox.id = candidate.id
            RETURNING outbox.id, outbox.route_provider_id,
                      outbox.protected_payload;
            """);
        command.Parameters.AddWithValue(NpgsqlDbType.Uuid, leaseId);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Interval,
            options.LeaseDuration);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var messageId = reader.GetGuid(0);
        var providerId = reader.GetString(1);
        var protectedPayload = reader.GetFieldValue<byte[]>(2);
        try
        {
            var message = serializer.UnprotectAccountMessage(
                protectedPayload);
            if (message.MessageId != messageId
                || HelloAccountMessageValidator.Validate(
                    message,
                    DateTimeOffset.UtcNow) is not null)
            {
                await DeadLetterAsync(
                    messageId,
                    leaseId,
                    HelloDeliveryErrorCodes.InvalidMessage,
                    cancellationToken);
                return null;
            }

            return new HelloAccountMessageOutboxLease(
                leaseId,
                providerId,
                message);
        }
        catch (CryptographicException)
        {
            await DeadLetterAsync(
                messageId,
                leaseId,
                HelloDeliveryErrorCodes.InvalidMessage,
                cancellationToken);
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedPayload);
        }
    }

    public Task<OperationResult> CompleteAsync(
        HelloAccountMessageOutboxLease lease,
        CancellationToken cancellationToken)
        => FinishAsync(lease, errorCode: null, cancellationToken);

    public Task<OperationResult> FailAsync(
        HelloAccountMessageOutboxLease lease,
        string errorCode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        return FinishAsync(lease, errorCode, cancellationToken);
    }

    private async Task<OperationResult> FinishAsync(
        HelloAccountMessageOutboxLease lease,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var command = dataSource.CreateCommand(
                errorCode is null
                    ? """
                      DELETE FROM skopka_hello.account_message_outbox
                      WHERE id = $1 AND lease_id = $2;
                      """
                    : """
                      UPDATE skopka_hello.account_message_outbox
                      SET failed_at = CASE
                              WHEN attempts >= $3 OR expires_at <= CURRENT_TIMESTAMP
                                  THEN CURRENT_TIMESTAMP
                              ELSE NULL
                          END,
                          available_at = CASE
                              WHEN attempts >= $3 OR expires_at <= CURRENT_TIMESTAMP
                                  THEN available_at
                              ELSE CURRENT_TIMESTAMP + $4
                          END,
                          last_error_code = $5,
                          lease_id = NULL,
                          leased_until = NULL
                      WHERE id = $1 AND lease_id = $2
                      RETURNING failed_at;
                      """);
            command.Parameters.AddWithValue(
                NpgsqlDbType.Uuid,
                lease.Message.MessageId);
            command.Parameters.AddWithValue(
                NpgsqlDbType.Uuid,
                lease.LeaseId);
            if (errorCode is not null)
            {
                command.Parameters.AddWithValue(
                    NpgsqlDbType.Integer,
                    options.MaximumAttempts);
                command.Parameters.AddWithValue(
                    NpgsqlDbType.Interval,
                    options.RetryDelay);
                command.Parameters.AddWithValue(
                    NpgsqlDbType.Varchar,
                    TruncateErrorCode(errorCode));
            }

            object? failureState = null;
            var affected = errorCode is null
                ? await command.ExecuteNonQueryAsync(cancellationToken)
                : (failureState = await command.ExecuteScalarAsync(
                    cancellationToken)) is null
                    ? 0
                    : 1;
            if (affected == 0)
            {
                return Failed();
            }

            if (errorCode is null)
            {
                HelloServerDiagnostics.AccountMessagesDelivered.Add(
                    1,
                    new KeyValuePair<string, object?>(
                        "message.kind",
                        lease.Message.Kind.ToString()));
            }
            else
            {
                var metric = failureState is DBNull
                    ? HelloServerDiagnostics.DeliveryRetries
                    : HelloServerDiagnostics.DeadLetters;
                metric.Add(
                    1,
                    new KeyValuePair<string, object?>(
                        "stage",
                        "provider"));
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
                    "message.finish"));
            return Failed();
        }
    }

    private async Task DeadLetterAsync(
        Guid messageId,
        Guid leaseId,
        string errorCode,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE skopka_hello.account_message_outbox
            SET failed_at = CURRENT_TIMESTAMP,
                last_error_code = $3,
                lease_id = NULL,
                leased_until = NULL
            WHERE id = $1 AND lease_id = $2;
            """);
        command.Parameters.AddWithValue(NpgsqlDbType.Uuid, messageId);
        command.Parameters.AddWithValue(NpgsqlDbType.Uuid, leaseId);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Varchar,
            errorCode);
        await command.ExecuteNonQueryAsync(cancellationToken);
        OutboxPayloadRejected(logger, messageId, errorCode, null);
        HelloServerDiagnostics.DeadLetters.Add(
            1,
            new KeyValuePair<string, object?>(
                "stage",
                "provider"));
    }

    private static string TruncateErrorCode(string errorCode)
        => errorCode.Length <= 128
            ? errorCode
            : errorCode[..128];

    private static OperationResult Failed()
        => OperationResultFactory.Fail(
            new Error(
                HelloDeliveryErrorCodes.Failed,
                "The durable account-message outbox failed.",
                ErrorType.Failure));

    [LoggerMessage(
        EventId = 2111,
        Level = LogLevel.Error,
        Message = "Could not persist account message {messageId}; kind: {messageKind}.")]
    private static partial void OutboxWriteFailed(
        ILogger logger,
        Guid messageId,
        HelloAccountMessageKind messageKind,
        Exception? exception);

    [LoggerMessage(
        EventId = 2112,
        Level = LogLevel.Error,
        Message = "Rejected protected account-message payload {messageId}; error code: {errorCode}.")]
    private static partial void OutboxPayloadRejected(
        ILogger logger,
        Guid messageId,
        string errorCode,
        Exception? exception);
}
