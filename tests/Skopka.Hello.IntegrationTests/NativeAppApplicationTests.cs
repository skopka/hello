using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello;
using Skopka.Hello.AuthorizationServer;
using Skopka.Identity.RateLimiting;
using Skopka.Identity.Sessions;
using Skopka.Identity.Users;
using Xunit;

namespace Skopka.Hello.IntegrationTests;

public sealed class NativeAppApplicationTests
{
    private const string Callback = "com.example.sample://callback";
    private const string Verifier = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~";
    private static string Challenge => Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier))).TrimEnd('=').Replace('+',
        '-').Replace('/',
        '_');

    [Fact]
    public async Task BindingReplayAndSourceRevocationAreEnforced()
    {
        await using var host = await Host.Create();
        var app = host.App;
        var begin = await app.BeginAsync(new("native", Callback, "state", Challenge, "/home", new()), default);
        Assert.True(begin.IsSuccess);
        Assert.False((await app.AttachAsync(begin.Value.FlowId, "wrong", null, default)).IsSuccess);
        var attach = await app.AttachAsync(begin.Value.FlowId, begin.Value.LaunchSecret, null, default);
        Assert.True(attach.IsSuccess);
        Assert.False((await app.AttachAsync(begin.Value.FlowId, begin.Value.LaunchSecret, null, default)).IsSuccess);
        Assert.False((await app.ConfirmAsync(begin.Value.FlowId, "wrong", "token", host.SessionId, true, null, default)).IsSuccess);
        var approved = await app.ConfirmAsync(begin.Value.FlowId,
            attach.Value.BrowserBinder,
            "token",
            host.SessionId,
            true,
            null,
            default);
        Assert.True(approved.IsSuccess);
        Assert.Equal(Callback, approved.Value.Callback);
        Assert.Equal("state", approved.Value.State);
        Assert.False((await app.CompleteAsync(begin.Value.FlowId, begin.Value.WebViewBinder,
            Verifier, "", null, default)).IsSuccess);
        Assert.False((await app.CompleteAsync(begin.Value.FlowId, begin.Value.WebViewBinder,
            Verifier, new string('A', 43), null, default)).IsSuccess);
        Assert.Equal(0, host.Identity.Created);
        var other = await app.BeginAsync(new("native", Callback, "other", Challenge, null, new()), default);
        var otherAttach = await app.AttachAsync(other.Value.FlowId, other.Value.LaunchSecret, null, default);
        var otherApproval = await app.ConfirmAsync(other.Value.FlowId, otherAttach.Value.BrowserBinder,
            "token", host.SessionId, true, null, default);
        Assert.True(otherApproval.IsSuccess);
        Assert.False((await app.CompleteAsync(begin.Value.FlowId, begin.Value.WebViewBinder,
            Verifier, otherApproval.Value.CompletionReceipt!, null, default)).IsSuccess);
        Assert.False((await app.CompleteAsync(begin.Value.FlowId, "wrong", Verifier, approved.Value.CompletionReceipt!, null, default)).IsSuccess);
        Assert.False((await app.CompleteAsync(begin.Value.FlowId,
            begin.Value.WebViewBinder,
            Verifier + "x",
            approved.Value.CompletionReceipt!,
            null,
            default)).IsSuccess);
        host.Identity.Valid = false;
        Assert.False((await app.CompleteAsync(begin.Value.FlowId, begin.Value.WebViewBinder, Verifier, approved.Value.CompletionReceipt!, null, default)).IsSuccess);
        host.Identity.Valid = true;
        var complete = await app.CompleteAsync(begin.Value.FlowId, begin.Value.WebViewBinder, Verifier, approved.Value.CompletionReceipt!, null, default);
        Assert.True(complete.IsSuccess);
        Assert.Equal(1, host.Identity.Created);
        Assert.False((await app.CompleteAsync(begin.Value.FlowId, begin.Value.WebViewBinder, Verifier, approved.Value.CompletionReceipt!, null, default)).IsSuccess);
        Assert.Equal(1, host.Identity.Created);
        Assert.All(host.Audit.Events, e =>
        {
            var serialized = JsonSerializer.Serialize(e);
            Assert.DoesNotContain(Verifier, serialized);
            Assert.DoesNotContain(approved.Value.CompletionReceipt!, serialized);
        });
    }

    [Fact]
    public async Task ClientPolicyAndRateLimitsAreEnforced()
    {
        await using var host = await Host.Create();
        var app = host.App;
        Assert.False((await app.BeginAsync(new("native",
            Callback,
            "state",
            Challenge,
            "https://evil.example/",
            new()),
            default)).IsSuccess);
        Assert.False((await app.BeginAsync(new("native",
            "com.example.sample://other",
            "state",
            Challenge,
            null,
            new()),
            default)).IsSuccess);
        Assert.False((await app.BeginAsync(new("native", Callback, "state", new string('.', 43), null, new()), default)).IsSuccess);
        host.Options.Enabled = false;
        Assert.False((await app.BeginAsync(new("native", Callback, "state", Challenge, null, new()), default)).IsSuccess);
        host.Options.Enabled = true;
        host.Limiter.Deny = true;
        var denied = await app.BeginAsync(new("native", Callback, "state", Challenge, null, new()), default);
        Assert.False(denied.IsSuccess);
        Assert.Contains(denied.Errors, e => e.Code == "identity.rate_limit.exceeded");
        Assert.Contains(host.Audit.Events, e => e.EventType == "hello.native_app.rate_limited");
    }

    [Fact]
    public async Task ContinuationUsesExistingLocalUrlRules()
    {
        await using var host = await Host.Create();
        var app = host.App;
        foreach (var path in new[] { "/home", "~/home", "~/" })
        {
            var result = await app.BeginAsync(
                new("native", Callback, "state", Challenge, path, new()), default);
            Assert.True(result.IsSuccess);
        }

        foreach (var path in new[] { "~//evil.example", "~/\\evil.example", "//evil.example", "/\\evil.example", "~/bad\npath" })
        {
            var result = await app.BeginAsync(
                new("native", Callback, "state", Challenge, path, new()), default);
            Assert.False(result.IsSuccess);
        }
    }

    [Fact]
    public async Task ExpiredLaunchAndChangedClientRevisionFailClosed()
    {
        await using var host = await Host.Create();
        var app = host.App;
        var first = await app.BeginAsync(new("native", Callback, "state", Challenge, null, new()), default);
        Assert.True(first.IsSuccess);
        host.Clock.Now = host.Clock.Now.AddMinutes(2);
        Assert.False((await app.AttachAsync(first.Value.FlowId, first.Value.LaunchSecret, null, default)).IsSuccess);
        var second = await app.BeginAsync(new("native", Callback, "state", Challenge, null, new()), default);
        Assert.True(second.IsSuccess);
        var attached = await app.AttachAsync(second.Value.FlowId, second.Value.LaunchSecret, null, default);
        Assert.True(attached.IsSuccess);
        var manager = host.Manager;
        var client = (await manager.FindByClientIdAsync("native"))!;
        var descriptor = new OpenIddictApplicationDescriptor();
        await manager.PopulateAsync(descriptor, client);
        descriptor.Properties["skopka_hello_client"] = JsonSerializer.SerializeToElement(new
        {
            Ownership = "administration",
            Revision = 2L,
            Resource = "resource",
            HonorReauthenticationPrompts = true,
            Scopes = Array.Empty<string>()
        });
        await manager.UpdateAsync(client, descriptor);
        Assert.False((await app.ConfirmAsync(second.Value.FlowId,
            attached.Value.BrowserBinder,
            "token",
            host.SessionId,
            true,
            null,
            default)).IsSuccess);
    }
    [Fact]
    public async Task ExpiredApprovalAndChangedSecurityStampCannotCreateSessions()
    {
        await using var host = await Host.Create();
        var app = host.App;
        var first = await app.BeginAsync(new("native", Callback, "state", Challenge, null, new()), default);
        var attached = await app.AttachAsync(first.Value.FlowId, first.Value.LaunchSecret, null, default);
        Assert.False((await app.ConfirmAsync(first.Value.FlowId,
            attached.Value.BrowserBinder,
            "invalid",
            host.SessionId,
            true,
            null,
            default)).IsSuccess);
        Assert.False((await app.ConfirmAsync(first.Value.FlowId,
            attached.Value.BrowserBinder,
            "token",
            Guid.NewGuid(),
            true,
            null,
            default)).IsSuccess);
        var approved = await app.ConfirmAsync(first.Value.FlowId,
            attached.Value.BrowserBinder, "token", host.SessionId, true, null, default);
        Assert.True(approved.IsSuccess);
        host.Identity.Stamp = "changed";
        Assert.False((await app.CompleteAsync(first.Value.FlowId, first.Value.WebViewBinder, Verifier, approved.Value.CompletionReceipt!, null, default)).IsSuccess);
        host.Identity.Stamp = "stamp";
        host.Clock.Now = host.Clock.Now.AddMinutes(2);
        Assert.False((await app.CompleteAsync(first.Value.FlowId, first.Value.WebViewBinder, Verifier, approved.Value.CompletionReceipt!, null, default)).IsSuccess);
        Assert.Equal(0, host.Identity.Created);
    }

    [Fact]
    public async Task FailedSessionCreationConsumesTheApproval()
    {
        await using var host = await Host.Create();
        var app = host.App;
        var begin = await app.BeginAsync(new("native", Callback, "state", Challenge, null, new()), default);
        var attach = await app.AttachAsync(begin.Value.FlowId, begin.Value.LaunchSecret, null, default);
        var approved = await app.ConfirmAsync(begin.Value.FlowId,
            attach.Value.BrowserBinder, "token", host.SessionId, true, null, default);
        Assert.True(approved.IsSuccess);
        host.Identity.FailCreate = true;
        Assert.False((await app.CompleteAsync(begin.Value.FlowId, begin.Value.WebViewBinder, Verifier, approved.Value.CompletionReceipt!, null, default)).IsSuccess);
        host.Identity.FailCreate = false;
        Assert.False((await app.CompleteAsync(begin.Value.FlowId, begin.Value.WebViewBinder, Verifier, approved.Value.CompletionReceipt!, null, default)).IsSuccess);
        Assert.Equal(1, host.Identity.Created);
    }
    [Fact]
    public async Task ConcurrentCompletionsCreateOnlyOneSession()
    {
        await using var host = await Host.Create();
        var first = host.App;
        var begin = await first.BeginAsync(new("native", Callback, "state", Challenge, null, new()), default);
        var attach = await first.AttachAsync(begin.Value.FlowId, begin.Value.LaunchSecret, null, default);
        var approved = await first.ConfirmAsync(begin.Value.FlowId,
            attach.Value.BrowserBinder, "token", host.SessionId, true, null, default);
        Assert.True(approved.IsSuccess);
        await using var other = host.OpenScope();
        var second = other.ServiceProvider.GetRequiredService<IHelloNativeAppSignInApplication<Profile>>();
        var results = await Task.WhenAll(
            first.CompleteAsync(begin.Value.FlowId, begin.Value.WebViewBinder, Verifier, approved.Value.CompletionReceipt!, null, default),
            second.CompleteAsync(begin.Value.FlowId, begin.Value.WebViewBinder, Verifier, approved.Value.CompletionReceipt!, null, default));
        Assert.Single(results, result => result.IsSuccess);
        Assert.Equal(1, host.Identity.Created);
    }

    [Fact]
    public async Task ConfidentialClientIsRejectedEvenWhenCallbackIsAllowed()
    {
        await using var host = await Host.Create();
        const string httpsCallback = "https://client.example/callback";
        host.Options.AllowedCallbacks["confidential"] = [httpsCallback];
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = "confidential",
            ClientType = "confidential",
            ClientSecret = "test-secret",
            DisplayName = "Confidential"
        };
        descriptor.RedirectUris.Add(new Uri(httpsCallback));
        descriptor.Properties["skopka_hello_client"] = JsonSerializer.SerializeToElement(new
        {
            Ownership = "administration",
            Revision = 1L,
            Resource = "resource",
            HonorReauthenticationPrompts = true,
            Scopes = Array.Empty<string>()
        });
        await host.Manager.CreateAsync(descriptor);
        Assert.False((await host.App.BeginAsync(new("confidential",
            httpsCallback,
            "state",
            Challenge,
            null,
            new()),
            default)).IsSuccess);
    }
    private sealed class Host : IAsyncDisposable
    {
        private readonly ServiceProvider provider;
        private readonly AsyncServiceScope scope;
        private readonly SqliteConnection connection;
        private Host(ServiceProvider provider,
            AsyncServiceScope scope,
            SqliteConnection connection,
            IdentityProxy identity,
            Limiter limiter,
            AuditSink audit,
            HelloNativeAppSignInOptions options)
        {
            this.provider = provider;
            this.scope = scope;
            this.connection = connection;
            Identity = identity;
            Limiter = limiter;
            Audit = audit;
            Options = options;
        }
        public IHelloNativeAppSignInApplication<Profile> App => scope.ServiceProvider.GetRequiredService<IHelloNativeAppSignInApplication<Profile>>();
        public IdentityProxy Identity { get; }
        public Limiter Limiter { get; }
        public AuditSink Audit { get; }
        public HelloNativeAppSignInOptions Options { get; }
        public AsyncServiceScope OpenScope() => provider.CreateAsyncScope();
        public Guid SessionId => Identity.SessionId;
        public IOpenIddictApplicationManager Manager => scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        public FakeClock Clock => provider.GetRequiredService<FakeClock>();
        public static async Task<Host> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            var clock = new FakeClock();
            services.AddSingleton(clock);
            services.AddSingleton<TimeProvider>(clock);
            services.AddDbContext<Context>(o =>
            {
                o.UseSqlite(connection);
                o.UseOpenIddict();
            });
            services.AddOpenIddict().AddCore(o => o.UseEntityFrameworkCore().UseDbContext<Context>());
            var session = DispatchProxy.Create<IIdentitySessionService<Profile>, IdentityProxy>();
            var identity = (IdentityProxy)session;
            var registry = DispatchProxy.Create<IIdentitySessionRegistry<Profile>, IdentityProxy>();
            ((IdentityProxy)registry).Shared = identity;
            services.AddSingleton(session);
            services.AddSingleton(registry);
            services.AddSingleton<IHelloAccessTokenValidator<Profile>>(new Validator(identity));
            var limiter = new Limiter();
            services.AddSingleton<IIdentityRateLimiter<Profile>>(limiter);
            var audit = new AuditSink();
            services.AddSingleton<IHelloSecurityEventSink>(audit);
            services.AddHelloNativeAppSignIn<Profile, Context>(o =>
            {
                o.Enabled = true;
                o.AllowedCallbacks["native"] = [Callback];
            });
            var provider = services.BuildServiceProvider();
            var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<Context>().Database.EnsureCreatedAsync();
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = "native",
                ClientType = "public",
                DisplayName = "Native"
            };
            descriptor.RedirectUris.Add(new Uri(Callback));
            descriptor.Properties["skopka_hello_client"] = JsonSerializer.SerializeToElement(new
            {
                Ownership = "administration",
                Revision = 1L,
                Resource = "resource",
                HonorReauthenticationPrompts = true,
                Scopes = Array.Empty<string>()
            });
            await manager.CreateAsync(descriptor);
            return new Host(provider,
                scope,
                connection,
                identity,
                limiter,
                audit,
                provider.GetRequiredService<HelloNativeAppSignInOptions>());
        }
        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
    private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            base.OnModelCreating(model);
            model.UseOpenIddict();
            model.AddHelloNativeAppFlows();
        }
    }
    public sealed record Profile;
    public class IdentityProxy : DispatchProxy
    {
        public IdentityProxy? Shared { get; set; }
        public Guid SessionId { get; } = Guid.NewGuid();
        public Guid UserId { get; } = Guid.NewGuid();
        public bool Valid { get; set; } = true;
        public bool FailCreate { get; set; }
        public string Stamp { get; set; } = "stamp";
        public int Created { get; set; }
        public IdentityUser<Profile> User => new(UserId,
            UserFlags.None,
            "alice",
            null,
            false,
            null,
            false,
            new(),
            1,
            Stamp,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var self = Shared ?? this;
            return targetMethod!.Name switch
            {
                "ValidateAsync" => Task.FromResult(self.Valid && ((ValidateIdentitySessionCommand)args![0]!).SessionId == self.SessionId ? OperationResultFactory.Success(self.User) : OperationResultFactory.Fail<IdentityUser<Profile>>(new Error("invalid",
                    "invalid",
                    ErrorType.Unauthorized))),
                "CreateAsync" => Create(self),
                "RevokeByIdAsync" => Task.FromResult(OperationResultFactory.Success()),
                _ => throw new NotSupportedException(targetMethod.Name),
            };
        }
        private static Task<OperationResult<IssuedIdentitySession>> Create(IdentityProxy self)
        {
            self.Created++;
            if (self.FailCreate) return Task.FromResult(OperationResultFactory.Fail<IssuedIdentitySession>(new Error("invalid",
                "invalid",
                ErrorType.Unauthorized)));
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(OperationResultFactory.Success(new IssuedIdentitySession(Guid.NewGuid(),
                "access",
                now.AddMinutes(5),
                "refresh",
                now.AddDays(1))));
        }
    }
    private sealed class Validator(IdentityProxy identity) : IHelloAccessTokenValidator<Profile>
    {
        public Task<OperationResult<IdentityUser<Profile>>> ValidateAsync(string token,
        CancellationToken cancellationToken) => Task.FromResult(token == "token" ? OperationResultFactory.Success(identity.User) : OperationResultFactory.Fail<IdentityUser<Profile>>(new Error("invalid",
        "invalid",
        ErrorType.Unauthorized)));
    }
    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Limiter : IIdentityRateLimiter<Profile>
    {
        public bool Deny { get; set; }
        public Task<RateLimitDecision> CheckAsync(RateLimitRequest request,
            CancellationToken cancellationToken) => HitAsync(request,
            cancellationToken);
        public Task<RateLimitDecision> HitAsync(RateLimitRequest request,
            CancellationToken cancellationToken) => Task.FromResult(new RateLimitDecision(!Deny,
            Deny ? DateTimeOffset.UtcNow.AddMinutes(1) : null));
        public Task ResetAsync(string scope, string key, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<int> PruneAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }
    private sealed class AuditSink : IHelloSecurityEventSink
    {
        public List<HelloSecurityEventEnvelope> Events { get; } = [];
        public OperationResult Write(HelloSecurityEventEnvelope securityEvent)
        {
            Events.Add(securityEvent);
            return OperationResultFactory.Success();
        }
    }
}
