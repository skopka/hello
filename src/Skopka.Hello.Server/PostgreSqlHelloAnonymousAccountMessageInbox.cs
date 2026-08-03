using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.Server;

internal sealed partial class PostgreSqlHelloAnonymousAccountMessageInbox(
    NpgsqlDataSource dataSource,
    HelloProtectedPayloadSerializer serializer,
    HelloServerPersistenceOptions options,
    ILogger<PostgreSqlHelloAnonymousAccountMessageInbox> logger)
    : IHelloAnonymousAccountMessageInbox
{
    public async Task<OperationResult> EnqueueAsync(
        HelloAnonymousAccountMessageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.MessageId == Guid.Empty
            || !Enum.IsDefined(request.Kind)
            || string.IsNullOrWhiteSpace(request.NormalizedTarget))
        {
            return InvalidMessage();
        }

        byte[] payload;
        try
        {
            payload = serializer.ProtectAnonymousRequest(request);
        }
        catch (CryptographicException)
        {
            return Failed();
        }

        try
        {
            await using var command = dataSource.CreateCommand(
                """
                INSERT INTO skopka_hello.anonymous_account_message_inbox
                    (id, kind, protected_payload, created_at, available_at,
                     expires_at)
                VALUES ($1, $2, $3, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, $4)
                ON CONFLICT (id) DO NOTHING;
                """);
            command.Parameters.AddWithValue(
                NpgsqlDbType.Uuid,
                request.MessageId);
            command.Parameters.AddWithValue(
                NpgsqlDbType.Smallint,
                (short)request.Kind);
            command.Parameters.AddWithValue(
                NpgsqlDbType.Bytea,
                payload);
            command.Parameters.AddWithValue(
                NpgsqlDbType.TimestampTz,
                DateTimeOffset.UtcNow + options.AnonymousRequestLifetime);
            var affected = await command.ExecuteNonQueryAsync(
                cancellationToken);
            if (affected > 0)
            {
                HelloServerDiagnostics.AnonymousRequestsPersisted.Add(1);
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
            AnonymousInboxWriteFailed(logger, request.MessageId, null);
            HelloServerDiagnostics.PersistenceFailures.Add(
                1,
                new KeyValuePair<string, object?>(
                    "operation",
                    "anonymous.enqueue"));
            return Failed();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public async IAsyncEnumerable<HelloAnonymousAccountMessageLease>
        ReadAllAsync(
            [EnumeratorCancellation]
            CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HelloAnonymousAccountMessageLease? lease;
            try
            {
                lease = await TryLeaseAsync(cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
            catch (NpgsqlException)
            {
                AnonymousInboxReadFailed(logger, null);
                HelloServerDiagnostics.PersistenceFailures.Add(
                    1,
                    new KeyValuePair<string, object?>(
                        "operation",
                        "anonymous.lease"));
                await DelayAsync(cancellationToken);
                continue;
            }

            if (lease is null)
            {
                await DelayAsync(cancellationToken);
                continue;
            }

            yield return lease;
        }
    }

    public Task<OperationResult> CompleteAsync(
        HelloAnonymousAccountMessageLease lease,
        CancellationToken cancellationToken)
        => FinishAsync(
            lease,
            errorCode: null,
            cancellationToken);

    public Task<OperationResult> FailAsync(
        HelloAnonymousAccountMessageLease lease,
        string errorCode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        return FinishAsync(lease, errorCode, cancellationToken);
    }

    private async Task<HelloAnonymousAccountMessageLease?> TryLeaseAsync(
        CancellationToken cancellationToken)
    {
        var leaseId = Guid.NewGuid();
        await using var command = dataSource.CreateCommand(
            """
            WITH candidate AS
            (
                SELECT id
                FROM skopka_hello.anonymous_account_message_inbox
                WHERE failed_at IS NULL
                  AND expires_at > CURRENT_TIMESTAMP
                  AND available_at <= CURRENT_TIMESTAMP
                  AND (leased_until IS NULL OR leased_until < CURRENT_TIMESTAMP)
                ORDER BY created_at, id
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            UPDATE skopka_hello.anonymous_account_message_inbox AS inbox
            SET lease_id = $1,
                leased_until = CURRENT_TIMESTAMP + $2,
                attempts = attempts + 1
            FROM candidate
            WHERE inbox.id = candidate.id
            RETURNING inbox.id, inbox.protected_payload;
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
        var protectedPayload = reader.GetFieldValue<byte[]>(1);
        try
        {
            var request = serializer.UnprotectAnonymousRequest(
                protectedPayload);
            if (request.MessageId != messageId
                || request.MessageId == Guid.Empty
                || !Enum.IsDefined(request.Kind)
                || string.IsNullOrWhiteSpace(request.NormalizedTarget))
            {
                await DeadLetterAsync(
                    messageId,
                    leaseId,
                    HelloDeliveryErrorCodes.InvalidMessage,
                    cancellationToken);
                return null;
            }

            return new HelloAnonymousAccountMessageLease(
                leaseId,
                request);
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

    private async Task<OperationResult> FinishAsync(
        HelloAnonymousAccountMessageLease lease,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();
        if (lease.LeaseId == Guid.Empty
            || lease.Request.MessageId == Guid.Empty)
        {
            return InvalidMessage();
        }

        try
        {
            await using var command = dataSource.CreateCommand(
                errorCode is null
                    ? """
                      DELETE FROM skopka_hello.anonymous_account_message_inbox
                      WHERE id = $1 AND lease_id = $2;
                      """
                    : """
                      UPDATE skopka_hello.anonymous_account_message_inbox
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
                lease.Request.MessageId);
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
                HelloServerDiagnostics.AnonymousRequestsCompleted.Add(1);
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
                        "anonymous"));
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
                    "anonymous.finish"));
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
            UPDATE skopka_hello.anonymous_account_message_inbox
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
        AnonymousInboxPayloadRejected(logger, messageId, errorCode, null);
        HelloServerDiagnostics.DeadLetters.Add(
            1,
            new KeyValuePair<string, object?>(
                "stage",
                "anonymous"));
    }

    private async Task DelayAsync(CancellationToken cancellationToken)
    {
        if (options.PollingInterval == TimeSpan.Zero)
        {
            await Task.Yield();
            return;
        }

        await Task.Delay(options.PollingInterval, cancellationToken);
    }

    private static string TruncateErrorCode(string errorCode)
        => errorCode.Length <= 128
            ? errorCode
            : errorCode[..128];

    private static OperationResult InvalidMessage()
        => OperationResultFactory.Fail(
            new Error(
                HelloDeliveryErrorCodes.InvalidMessage,
                "The anonymous account-message request is invalid.",
                ErrorType.Failure));

    private static OperationResult Failed()
        => OperationResultFactory.Fail(
            new Error(
                HelloDeliveryErrorCodes.Failed,
                "The durable account-message inbox failed.",
                ErrorType.Failure));

    [LoggerMessage(
        EventId = 2101,
        Level = LogLevel.Error,
        Message = "Could not persist anonymous account-message request {messageId}.")]
    private static partial void AnonymousInboxWriteFailed(
        ILogger logger,
        Guid messageId,
        Exception? exception);

    [LoggerMessage(
        EventId = 2102,
        Level = LogLevel.Error,
        Message = "Could not lease an anonymous account-message request.")]
    private static partial void AnonymousInboxReadFailed(
        ILogger logger,
        Exception? exception);

    [LoggerMessage(
        EventId = 2103,
        Level = LogLevel.Error,
        Message = "Rejected protected anonymous account-message payload {messageId}; error code: {errorCode}.")]
    private static partial void AnonymousInboxPayloadRejected(
        ILogger logger,
        Guid messageId,
        string errorCode,
        Exception? exception);
}
