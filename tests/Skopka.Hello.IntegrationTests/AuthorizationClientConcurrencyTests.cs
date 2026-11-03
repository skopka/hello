using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.AuthorizationServer;
using Xunit;

namespace Skopka.Hello.IntegrationTests;

public sealed class AuthorizationClientConcurrencyTests
{
    private static readonly CancellationToken Cancellation = CancellationToken.None;

    [Fact]
    public async Task DuplicateCreateFromIndependentScopesHasOneWinner()
    {
        await using var host = await ClientHost.CreateAsync();
        await using var first = host.Services.CreateAsyncScope();
        await using var second = host.Services.CreateAsyncScope();
        var a = first.ServiceProvider.GetRequiredService<IHelloAuthorizationClientManagement>();
        var b = second.ServiceProvider.GetRequiredService<IHelloAuthorizationClientManagement>();
        var results = await Task.WhenAll(
            Task.Run(() => a.CreateAsync(Input("duplicate"), Cancellation), Cancellation),
            Task.Run(() => b.CreateAsync(Input("duplicate"), Cancellation), Cancellation));

        Assert.Single(results, result => result.IsSuccess);
        var failure = Assert.Single(results, result => !result.IsSuccess);
        Assert.Contains(failure.Errors, error => error.Type == ErrorType.Conflict);
    }

    [Fact]
    public async Task StaleUpdateAndDeleteCannotOverrideACommittedChange()
    {
        await using var host = await ClientHost.CreateAsync();
        await using var first = host.Services.CreateAsyncScope();
        await using var second = host.Services.CreateAsyncScope();
        await using var third = host.Services.CreateAsyncScope();
        var a = first.ServiceProvider.GetRequiredService<IHelloAuthorizationClientManagement>();
        var b = second.ServiceProvider.GetRequiredService<IHelloAuthorizationClientManagement>();
        var c = third.ServiceProvider.GetRequiredService<IHelloAuthorizationClientManagement>();
        var created = await a.CreateAsync(Input("stale"), Cancellation);
        Assert.True(created.IsSuccess);
        var revision = created.Value.Client.Revision;
        Assert.True((await b.GetAsync("stale", Cancellation)).IsSuccess);
        Assert.True((await c.GetAsync("stale", Cancellation)).IsSuccess);
        Assert.True((await a.UpdateAsync("stale", revision, Input("stale") with { DisplayName = "New" }, Cancellation)).IsSuccess);
        var update = Task.Run(
            () => b.UpdateAsync("stale", revision,
                Input("stale") with { DisplayName = "Old" }, Cancellation), Cancellation);
        var delete = Task.Run(
            () => c.DeleteAsync("stale", revision, Cancellation), Cancellation);
        await Task.WhenAll(update, delete);
        var staleUpdate = await update;
        var staleDelete = await delete;
        Assert.False(staleUpdate.IsSuccess);
        Assert.Contains(staleUpdate.Errors, error => error.Type == ErrorType.Conflict);
        Assert.False(staleDelete.IsSuccess);
        Assert.Contains(staleDelete.Errors, error => error.Type == ErrorType.Conflict);
    }

    [Fact]
    public async Task RepeatedSyncPreservesRevisionAndConfidentialSecret()
    {
        await using var host = await ClientHost.CreateAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var sync = scope.ServiceProvider.GetRequiredService<IHelloAuthorizationClientSynchronizer>();
        var clients = scope.ServiceProvider.GetRequiredService<IHelloAuthorizationClientManagement>();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        await sync.SynchronizeAsync(Cancellation);
        var before = await clients.GetAsync("configured", Cancellation);
        var app = await manager.FindByClientIdAsync("configured", Cancellation);
        Assert.True(await manager.ValidateClientSecretAsync(app!, "test-secret", Cancellation));
        await sync.SynchronizeAsync(Cancellation);
        var after = await clients.GetAsync("configured", Cancellation);
        Assert.Equal(before.Value.Revision, after.Value.Revision);
        Assert.True(await manager.ValidateClientSecretAsync(app!, "test-secret", Cancellation));
    }

    [Fact]
    public async Task TaggedCorruptMetadataBlocksSyncBeforeAnyMutation()
    {
        await using var host = await ClientHost.CreateAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var sync = scope.ServiceProvider.GetRequiredService<IHelloAuthorizationClientSynchronizer>();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = "configured",
            ClientType = OpenIddictConstants.ClientTypes.Public,
            DisplayName = "Do not overwrite",
        };
        descriptor.Properties["skopka_hello_client"] = System.Text.Json.JsonSerializer.SerializeToElement("malformed");
        await manager.CreateAsync(descriptor, Cancellation);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sync.SynchronizeAsync(Cancellation));
        var app = await manager.FindByClientIdAsync("configured", Cancellation);
        Assert.Equal("Do not overwrite", await manager.GetDisplayNameAsync(app!, Cancellation));
    }

    private static HelloAuthorizationClientInput Input(string id) => new(
        id, "Test", HelloAuthorizationClientType.Public, "skopka-hello-api",
        ["https://example.test/callback"], [], ["openid"], true);

    private sealed class ClientHost : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private ClientHost(SqliteConnection connection, ServiceProvider services)
            => (this.connection, Services) = (connection, services);
        public ServiceProvider Services { get; }

        public static async Task<ClientHost> CreateAsync()
        {
            var connection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ClientDbContext>(options => options.UseSqlite(connection.ConnectionString).UseOpenIddict());
            services.AddOpenIddict().AddCore(options => options.UseEntityFrameworkCore().UseDbContext<ClientDbContext>());
            services.AddSkopkaHelloAuthorizationClients(options =>
            {
                options.Issuer = new Uri("https://example.test/");
                options.Clients.Add(new HelloAuthorizationClientOptions
                {
                    ClientId = "configured",
                    DisplayName = "Configured",
                    Type = HelloAuthorizationClientType.Confidential,
                    ClientSecret = "test-secret",
                    RedirectUris = ["https://example.test/callback"],
                    Scopes = ["openid"],
                });
            });
            var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ClientDbContext>().Database.EnsureCreatedAsync();
            return new ClientHost(connection, provider);
        }
        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class ClientDbContext(DbContextOptions<ClientDbContext> options) : DbContext(options);
}
