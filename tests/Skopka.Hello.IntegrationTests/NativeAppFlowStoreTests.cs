using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Skopka.Hello;
using Skopka.Hello.AuthorizationServer;
using Xunit;

namespace Skopka.Hello.IntegrationTests;

public sealed class NativeAppFlowStoreTests
{
    [Fact]
    public async Task CompetingTransitionsHaveExactlyOneWinnerAndPruningIsBounded()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FlowContext>().UseSqlite(connection).Options;
        await using var first = new FlowContext(options);
        await first.Database.EnsureCreatedAsync();
        var firstStore = new EfHelloNativeAppFlowStore<FlowContext>(first);
        var now = DateTimeOffset.UtcNow;
        await firstStore.AddAsync(Flow("one", now.AddMinutes(-2)), CancellationToken.None);
        await firstStore.AddAsync(Flow("two", now.AddMinutes(-1)), CancellationToken.None);
        await firstStore.AddAsync(Flow("live", now.AddMinutes(1)), CancellationToken.None);
        await using var second = new FlowContext(options);
        var secondStore = new EfHelloNativeAppFlowStore<FlowContext>(second);
        var a = (await firstStore.FindAsync("one", CancellationToken.None))!;
        var b = (await secondStore.FindAsync("one", CancellationToken.None))!;
        a.Status = HelloNativeAppSignInState.BrowserAttached;
        b.Status = HelloNativeAppSignInState.Cancelled;
        Assert.True(await firstStore.ReplaceAsync(a, a.Version, CancellationToken.None));
        Assert.False(await secondStore.ReplaceAsync(b, b.Version, CancellationToken.None));
        Assert.Equal(HelloNativeAppSignInState.BrowserAttached,
            (await secondStore.FindAsync("one", CancellationToken.None))!.Status);
        Assert.Equal(1, await firstStore.PruneAsync(now, 1, CancellationToken.None));
        Assert.Equal(1, await firstStore.PruneAsync(now, 1, CancellationToken.None));
        Assert.Equal(0, await firstStore.PruneAsync(now, 1, CancellationToken.None));
        Assert.NotNull(await firstStore.FindAsync("live", CancellationToken.None));
    }

    private static HelloNativeAppFlow Flow(string id, DateTimeOffset expiresAt) => new()
    {
        Id = id,
        ClientId = "client",
        Callback = "sample://callback",
        State = "state",
        Challenge = "challenge",
        WebViewBinderHash = "binder",
        LaunchSecretHash = "launch",
        SessionMetadata = "{}",
        CreatedAt = expiresAt.AddMinutes(-10),
        LaunchExpiresAt = expiresAt.AddMinutes(-9),
        ExpiresAt = expiresAt,
    };

    private sealed class FlowContext(DbContextOptions<FlowContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.AddHelloNativeAppFlows();
    }
}
