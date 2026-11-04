using Microsoft.EntityFrameworkCore;
using Skopka.Hello.AuthorizationServer;
using Skopka.Hello.Server;
using Testcontainers.PostgreSql;

namespace Skopka.Hello.IntegrationTests;

public sealed class NativeAppPersistenceTests
{
    [Fact]
    public async Task MigrationPersistsFlowsAcrossContextsAndAllowsOneConcurrentTransition()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<HelloAuthorizationDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).UseOpenIddict().Options;
        var now = DateTimeOffset.UtcNow;
        await using (var migration = new HelloAuthorizationDbContext(options))
        {
            await migration.Database.MigrateAsync();
            Assert.Empty(await migration.Database.GetPendingMigrationsAsync());
            var store = new EfHelloNativeAppFlowStore<HelloAuthorizationDbContext>(migration);
            await store.AddAsync(Flow("active", now.AddMinutes(5)), CancellationToken.None);
            await store.AddAsync(Flow("expired", now.AddDays(-2)), CancellationToken.None);
        }

        await using var first = new HelloAuthorizationDbContext(options);
        await using var second = new HelloAuthorizationDbContext(options);
        var leftStore = new EfHelloNativeAppFlowStore<HelloAuthorizationDbContext>(first);
        var rightStore = new EfHelloNativeAppFlowStore<HelloAuthorizationDbContext>(second);
        var left = Assert.IsType<HelloNativeAppFlow>(await leftStore.FindAsync("active", CancellationToken.None));
        var right = Assert.IsType<HelloNativeAppFlow>(await rightStore.FindAsync("active", CancellationToken.None));
        left.Status = HelloNativeAppSignInState.Consuming;
        right.Status = HelloNativeAppSignInState.Consuming;
        var results = await Task.WhenAll(leftStore.ReplaceAsync(left, 0, CancellationToken.None),
            rightStore.ReplaceAsync(right, 0, CancellationToken.None));
        Assert.Single(results, result => result);
        Assert.Equal(1, await leftStore.PruneAsync(now.AddDays(-1), 1, CancellationToken.None));
        Assert.Null(await rightStore.FindAsync("expired", CancellationToken.None));
        Assert.Equal(HelloNativeAppSignInState.Consuming,
            (await rightStore.FindAsync("active", CancellationToken.None))!.Status);
    }

    private static HelloNativeAppFlow Flow(string id, DateTimeOffset expires) => new()
    {
        Id = id,
        ClientId = "test-client",
        Callback = "test://callback",
        State = "state",
        Challenge = "challenge",
        WebViewBinderHash = "binder",
        LaunchSecretHash = "launch",
        SessionMetadata = "{}",
        CreatedAt = expires.AddMinutes(-10),
        ExpiresAt = expires,
        LaunchExpiresAt = expires.AddMinutes(-9),
        Status = HelloNativeAppSignInState.Pending,
    };
}
