using Microsoft.Extensions.DependencyInjection;
using Skopka.Hello.Oidc;
using Skopka.Identity.RateLimiting;

namespace Skopka.Hello.Tests;

public sealed class HelloOidcFlowStoreTests
{
    [Fact]
    public async Task InMemoryFallbackConsumesOnlyOnceAtomically()
    {
        await using var services = CreateServices().BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<
            IHelloOidcFlowStore>();
        var flowId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);

        var attempts = await Task.WhenAll(
            Enumerable.Range(0, 32).Select(_ =>
                store.TryConsumeAsync(
                    flowId,
                    expiresAt,
                    CancellationToken.None)));

        Assert.Single(attempts, consumed => consumed);
    }

    [Fact]
    public async Task PersistentIdentityRateLimiterIsPreferredWhenAvailable()
    {
        var limiter = new FakeIdentityRateLimiter();
        var registrations = CreateServices();
        registrations.AddSingleton<IIdentityRateLimiter<object>>(limiter);
        await using var services = registrations.BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<
            IHelloOidcFlowStore>();
        var flowId = Guid.NewGuid();

        var consumed = await store.TryConsumeAsync(
            flowId,
            DateTimeOffset.UtcNow.AddMinutes(5),
            CancellationToken.None);

        Assert.True(consumed);
        var request = Assert.Single(limiter.Hits);
        Assert.Equal("hello-oidc-flow", request.Scope);
        Assert.Equal(flowId.ToString("N"), request.Key);
        Assert.Equal(1, request.PermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(30), request.Window);
    }

    [Fact]
    public void CustomFlowStoreRegistrationIsPreserved()
    {
        var custom = new FakeFlowStore();
        var registrations = new ServiceCollection();
        registrations.AddSingleton<IHelloOidcFlowStore>(custom);
        AddOidc(registrations);
        using var services = registrations.BuildServiceProvider();
        using var scope = services.CreateScope();

        var resolved = scope.ServiceProvider.GetRequiredService<
            IHelloOidcFlowStore>();

        Assert.Same(custom, resolved);
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        AddOidc(services);
        return services;
    }

    private static void AddOidc(IServiceCollection services)
    {
        services.AddSkopkaHelloOidc<object>(options =>
        {
            options.PublicOrigin = new Uri(
                "https://hello.example.test");
            options.Providers["github"] = new HelloOidcProviderOptions
            {
                DisplayName = "GitHub",
                Authority = "https://accounts.example.test",
                ClientId = "hello-tests",
                ClientSecret = "not-a-production-secret",
            };
        });
    }

    private sealed class FakeFlowStore : IHelloOidcFlowStore
    {
        public Task<bool> TryConsumeAsync(
            Guid flowId,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private sealed class FakeIdentityRateLimiter
        : IIdentityRateLimiter<object>
    {
        public List<RateLimitRequest> Hits { get; } = [];

        public Task<RateLimitDecision> CheckAsync(
            RateLimitRequest request,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RateLimitDecision> HitAsync(
            RateLimitRequest request,
            CancellationToken ct)
        {
            Hits.Add(request);
            return Task.FromResult(new RateLimitDecision(true, null));
        }

        public Task ResetAsync(
            string scope,
            string key,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<int> PruneAsync(CancellationToken ct)
            => throw new NotSupportedException();
    }
}
