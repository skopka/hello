using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OpenIddict.Abstractions;
using Skopka.Hello.AuthorizationServer;
using Skopka.Hello.Server;
using Testcontainers.PostgreSql;

namespace Skopka.Hello.IntegrationTests;

public sealed class HelloServerPersistenceTests
{
    [Fact]
    public async Task AuthorizationMigrationAndClientSynchronizationUsePostgres()
    {
        await using var postgres = new PostgreSqlBuilder(
                "postgres:17-alpine")
            .Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<HelloAuthorizationDbContext>(options =>
        {
            options.UseNpgsql(
                connectionString,
                HelloAuthorizationDbContext.ConfigureNpgsql);
            options.UseOpenIddict();
        });
        services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore()
                .UseDbContext<HelloAuthorizationDbContext>());
        services.AddSkopkaHelloAuthorizationClients(options =>
        {
            options.Issuer = new Uri("https://hello.example.test");
            options.Clients.Add(new HelloAuthorizationClientOptions
            {
                ClientId = "postgres-native",
                DisplayName = "PostgreSQL native client",
                Type = HelloAuthorizationClientType.Public,
                RedirectUris = ["com.example.postgres:/callback"],
                Scopes = [OpenIddictConstants.Scopes.OpenId],
            });
        });

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<
            HelloAuthorizationDbContext>();
        await database.Database.MigrateAsync();
        Assert.Empty(await database.Database.GetPendingMigrationsAsync());

        var clients = scope.ServiceProvider.GetRequiredService<
            IHelloAuthorizationClientSynchronizer>();
        await clients.SynchronizeAsync(CancellationToken.None);
        var applications = scope.ServiceProvider.GetRequiredService<
            IOpenIddictApplicationManager>();
        var application = await applications.FindByClientIdAsync(
            "postgres-native");
        Assert.NotNull(application);
        Assert.Equal(
            OpenIddictConstants.ClientTypes.Public,
            await applications.GetClientTypeAsync(application));

        await using var dataSource = NpgsqlDataSource.Create(
            connectionString);
        Assert.Equal(
            1L,
            await CountAsync(
                dataSource,
                "skopka_hello_oauth.applications"));
        Assert.Equal(
            1L,
            await CountAsync(
                dataSource,
                "skopka_hello_oauth.schema_migrations"));
    }

    [Fact]
    public async Task MigrationQueuesAndAuditRoundTripProtectedData()
    {
        await using var postgres = new PostgreSqlBuilder(
                "postgres:17-alpine")
            .Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();

        var concurrentMigrations = await Task.WhenAll(
            HelloServerDatabaseMigrator.ApplyAsync(
                connectionString,
                CancellationToken.None),
            HelloServerDatabaseMigrator.ApplyAsync(
                connectionString,
                CancellationToken.None));
        Assert.Contains(1, concurrentMigrations);
        Assert.Contains(0, concurrentMigrations);
        Assert.Equal(
            0,
            await HelloServerDatabaseMigrator.ApplyAsync(
                connectionString,
                CancellationToken.None));

        await using var dataSource = NpgsqlDataSource.Create(
            connectionString);
        Assert.True(
            await HelloServerDatabaseMigrator.IsCurrentAsync(
                dataSource,
                CancellationToken.None));

        var persistenceOptions = new HelloServerPersistenceOptions
        {
            PollingInterval = TimeSpan.Zero,
        };
        var serializer = new HelloProtectedPayloadSerializer(
            new EphemeralDataProtectionProvider());
        var inbox = new PostgreSqlHelloAnonymousAccountMessageInbox(
            dataSource,
            serializer,
            persistenceOptions,
            NullLogger<
                PostgreSqlHelloAnonymousAccountMessageInbox>.Instance);
        var anonymousRequest = new HelloAnonymousAccountMessageRequest(
            Guid.NewGuid(),
            HelloAccountMessageKind.PasswordReset,
            "ALICE@EXAMPLE.TEST");

        Assert.True(
            (await inbox.EnqueueAsync(
                anonymousRequest,
                CancellationToken.None)).IsSuccess);
        var anonymousPayload = await ReadPayloadAsync(
            dataSource,
            "skopka_hello.anonymous_account_message_inbox",
            anonymousRequest.MessageId);
        Assert.DoesNotContain(
            anonymousRequest.NormalizedTarget,
            Encoding.UTF8.GetString(anonymousPayload),
            StringComparison.Ordinal);

        await using (var reader = inbox
            .ReadAllAsync(CancellationToken.None)
            .GetAsyncEnumerator(CancellationToken.None))
        {
            Assert.True(await reader.MoveNextAsync());
            Assert.Equal(anonymousRequest, reader.Current.Request);
            Assert.True(
                (await inbox.CompleteAsync(
                    reader.Current,
                    CancellationToken.None)).IsSuccess);
        }

        Assert.Equal(
            0L,
            await CountAsync(
                dataSource,
                "skopka_hello.anonymous_account_message_inbox"));

        var outbox = new PostgreSqlHelloAccountMessageOutbox(
            dataSource,
            serializer,
            persistenceOptions,
            new HelloDurableEmailRouteOptions("capture"),
            NullLogger<PostgreSqlHelloAccountMessageOutbox>.Instance);
        var accountMessage = new HelloAccountMessage(
            Guid.NewGuid(),
            HelloAccountMessageKind.PasswordChangeVerification,
            HelloDeliveryChannel.Email,
            "alice@example.test",
            null,
            DateTimeOffset.UtcNow.AddMinutes(5),
            "123456");

        Assert.True(
            (await outbox.SendAsync(
                accountMessage,
                CancellationToken.None)).IsSuccess);
        var accountPayload = await ReadPayloadAsync(
            dataSource,
            "skopka_hello.account_message_outbox",
            accountMessage.MessageId);
        var accountPayloadText = Encoding.UTF8.GetString(
            accountPayload);
        Assert.DoesNotContain(
            accountMessage.RecipientAddress,
            accountPayloadText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            accountMessage.VerificationCode!,
            accountPayloadText,
            StringComparison.Ordinal);

        var leasedMessage = await outbox.TryLeaseAsync(
            CancellationToken.None);
        Assert.NotNull(leasedMessage);
        Assert.Equal(accountMessage, leasedMessage.Message);
        Assert.Equal("capture", leasedMessage.DestinationProviderId);
        Assert.True(
            (await outbox.CompleteAsync(
                leasedMessage,
                CancellationToken.None)).IsSuccess);

        var captureProvider = new CaptureProvider();
        var worker = new PostgreSqlHelloAccountMessageWorker(
            outbox,
            [outbox, captureProvider],
            persistenceOptions,
            new HelloDurableEmailRouteOptions("capture"),
            NullLogger<
                PostgreSqlHelloAccountMessageWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var workerMessage = accountMessage with
            {
                MessageId = Guid.NewGuid(),
            };
            Assert.True(
                (await outbox.SendAsync(
                    workerMessage,
                    CancellationToken.None)).IsSuccess);
            using var deliveryTimeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(10));
            var delivered = await captureProvider.Delivered.Task.WaitAsync(
                deliveryTimeout.Token);
            Assert.Equal(workerMessage, delivered);
            await WaitForEmptyAsync(
                dataSource,
                "skopka_hello.account_message_outbox",
                deliveryTimeout.Token);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }

        var terminalOutbox = new PostgreSqlHelloAccountMessageOutbox(
            dataSource,
            serializer,
            new HelloServerPersistenceOptions
            {
                MaximumAttempts = 1,
                PollingInterval = TimeSpan.Zero,
            },
            new HelloDurableEmailRouteOptions("capture"),
            NullLogger<PostgreSqlHelloAccountMessageOutbox>.Instance);
        var failingMessage = accountMessage with
        {
            MessageId = Guid.NewGuid(),
        };
        Assert.True(
            (await terminalOutbox.SendAsync(
                failingMessage,
                CancellationToken.None)).IsSuccess);
        var failingLease = await terminalOutbox.TryLeaseAsync(
            CancellationToken.None);
        Assert.NotNull(failingLease);
        Assert.True(
            (await terminalOutbox.FailAsync(
                failingLease,
                HelloDeliveryErrorCodes.Failed,
                CancellationToken.None)).IsSuccess);
        Assert.Null(
            await terminalOutbox.TryLeaseAsync(
                CancellationToken.None));

        var audit = new PostgreSqlHelloAuditOutbox(dataSource);
        var auditEvent = new HelloSecurityEventEnvelope(
            Guid.NewGuid(),
            "identity.password.changed",
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "trace-123",
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>
            {
                ["channel"] = "password",
            });
        Assert.True(audit.Write(auditEvent).IsSuccess);
        Assert.True(audit.Write(auditEvent).IsSuccess);
        Assert.Equal(
            1L,
            await CountAsync(
                dataSource,
                "skopka_hello.audit_outbox"));

        await using (var corruptHistory = dataSource.CreateCommand(
            """
            UPDATE skopka_hello.schema_migrations
            SET checksum = repeat('0', 64)
            WHERE migration_id = $1;
            """))
        {
            corruptHistory.Parameters.AddWithValue(
                HelloServerDatabaseMigrator.LatestMigrationId);
            await corruptHistory.ExecuteNonQueryAsync();
        }

        Assert.False(
            await HelloServerDatabaseMigrator.IsCurrentAsync(
                dataSource,
                CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => HelloServerDatabaseMigrator.ApplyAsync(
                connectionString,
                CancellationToken.None));
    }

    private static async Task<byte[]> ReadPayloadAsync(
        NpgsqlDataSource dataSource,
        string tableName,
        Guid id)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT protected_payload
            FROM {tableName}
            WHERE id = $1;
            """);
        command.Parameters.AddWithValue(id);
        var value = await command.ExecuteScalarAsync();
        return Assert.IsType<byte[]>(value);
    }

    private static async Task<long> CountAsync(
        NpgsqlDataSource dataSource,
        string tableName)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT COUNT(*) FROM {tableName};");
        var value = await command.ExecuteScalarAsync();
        return Assert.IsType<long>(value);
    }

    private static async Task WaitForEmptyAsync(
        NpgsqlDataSource dataSource,
        string tableName,
        CancellationToken cancellationToken)
    {
        while (await CountAsync(dataSource, tableName) != 0)
        {
            await Task.Delay(10, cancellationToken);
        }
    }

    private sealed class CaptureProvider : IHelloAccountMessageProvider
    {
        public TaskCompletionSource<HelloAccountMessage> Delivered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ProviderId => "capture";

        public HelloDeliveryChannel Channel => HelloDeliveryChannel.Email;

        public Task<Skopka.Abstraction.OperationResult.OperationResult>
            SendAsync(
                HelloAccountMessage message,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delivered.TrySetResult(message);
            return Task.FromResult(
                Skopka.Abstraction.OperationResult
                    .OperationResultFactory.Success());
        }
    }
}
