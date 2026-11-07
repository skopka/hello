using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello;
using Skopka.Hello.WebAuthn;

namespace Skopka.Hello.Tests;

public sealed class HelloContinuationGateTests
{
    public sealed record Profile;
    public class IdentityProxy : DispatchProxy
    {
        public static readonly Guid UserId = Guid.NewGuid();
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IHelloIdentityApplication<Profile>.ValidateAccessTokenAsync))
                return Task.FromResult(OperationResultFactory.Success(new HelloAccount<Profile>(
                    UserId, default, "test", null, false, null, false, new Profile(), 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)));
            throw new NotSupportedException(targetMethod?.Name);
        }
    }
    public sealed class CookieHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, IdentityProxy.UserId.ToString())], Scheme.Name));
            var properties = new AuthenticationProperties();
            properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = "test" }]);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, properties, Scheme.Name)));
        }
    }
    private sealed class FlowStore : IHelloWebAuthnFlowStore
    {
        private readonly HashSet<Guid> used = [];
        public Task<bool> TryConsumeAsync(Guid id, DateTimeOffset expiresAt, CancellationToken cancellationToken)
            => Task.FromResult(used.Add(id));
    }
    private sealed class Continuation : IHelloRegistrationContinuation<Profile>
    {
        public bool Pending { get; set; } = true;
        public bool Fail { get; set; }
        public Task<OperationResult<Profile>> PrepareProfileAsync(Profile profile, HelloRegistrationKind kind, CancellationToken ct)
            => Task.FromResult(OperationResultFactory.Success(profile));
        public Task<OperationResult<bool>> IsPendingAsync(HelloAccount<Profile> account, CancellationToken ct)
            => Task.FromResult(Fail ? OperationResultFactory.Fail<bool>(new Error("test", "Failed", ErrorType.Forbidden)) : OperationResultFactory.Success(Pending));
    }
    private static (DefaultHttpContext Context, IHelloIdentityApplication<Profile> Identity, IDataProtectionProvider Protection) Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, CookieHandler>("Skopka.Hello.UI", _ => { });
        services.AddProblemDetails();
        services.AddSingleton<IHelloWebAuthnFlowStore, FlowStore>();
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Response.Body = new MemoryStream();
        ctx.Request.Headers.Authorization = "Bearer test";
        return (ctx, DispatchProxy.Create<IHelloIdentityApplication<Profile>, IdentityProxy>(), new EphemeralDataProtectionProvider());
    }

    [Fact]
    public async Task PendingBearerIsForbiddenAndExemptEndpointPasses()
    {
        var (ctx, identity, protection) = Setup();
        var called = false;
        var gate = new HelloContinuationGate<Profile>(_ => { called = true; return Task.CompletedTask; }, new());
        await gate.InvokeAsync(ctx, identity, new Continuation(), protection);
        Assert.Equal(403, ctx.Response.StatusCode);
        Assert.False(called);
        ctx.Response.StatusCode = 200;
        ctx.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new HelloContinuationExemptAttribute()), "host continuation"));
        await gate.InvokeAsync(ctx, identity, new Continuation(), protection);
        Assert.True(called);
    }

    [Fact]
    public async Task PolicyFailureFailsClosed()
    {
        var (ctx, identity, protection) = Setup();
        var gate = new HelloContinuationGate<Profile>(_ => throw new InvalidOperationException(), new());
        await gate.InvokeAsync(ctx, identity, new Continuation { Fail = true }, protection);
        Assert.Equal(503, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task CompletionRequiresHostDoneAndRejectsForgedCookie()
    {
        var (ctx, identity, protection) = Setup();
        ctx.Request.Headers.Cookie = "__Host-Skopka.Hello.Continuation=forged";
        var workflow = new Continuation();
        var denied = await HelloContinuationCompletion.CompleteAsync(ctx, identity, workflow, protection, new(), CancellationToken.None);
        Assert.False(denied.IsSuccess);
        workflow.Pending = false;
        var completed = await HelloContinuationCompletion.CompleteAsync(ctx, identity, workflow, protection, new(), CancellationToken.None);
        Assert.True(completed.IsSuccess);
        Assert.Equal("/", completed.Value);
    }
    [Fact]
    public async Task PendingBrowserPreservesReturnUntilHostCompletes()
    {
        var (ctx, identity, protection) = Setup();
        ctx.Request.Headers.Remove("Authorization");
        ctx.Request.Method = "GET";
        ctx.Request.Headers.Accept = "text/html";
        ctx.Request.Path = "/purchase";
        ctx.Request.QueryString = new QueryString("?item=1");
        var workflow = new Continuation();
        var gate = new HelloContinuationGate<Profile>(_ => throw new InvalidOperationException(), new());
        await gate.InvokeAsync(ctx, identity, workflow, protection);
        Assert.Equal("/onboarding", ctx.Response.Headers.Location.ToString());
        var cookie = Assert.Single(ctx.Response.Headers.SetCookie) ?? throw new InvalidOperationException();
        cookie = cookie.Split(';')[0];
        ctx.Request.Headers.Cookie = cookie;
        var denied = await HelloContinuationCompletion.CompleteAsync(ctx, identity, workflow, protection, new(), CancellationToken.None);
        Assert.False(denied.IsSuccess);
        workflow.Pending = false;
        var completed = await HelloContinuationCompletion.CompleteAsync(ctx, identity, workflow, protection, new(), CancellationToken.None);
        Assert.Equal("/purchase?item=1", completed.Value);
        var replay = await HelloContinuationCompletion.CompleteAsync(ctx, identity, workflow, protection, new(), CancellationToken.None);
        Assert.False(replay.IsSuccess);
    }

    [Fact]
    public async Task EmptyBearerNeverFallsBackToUiCookie()
    {
        var (ctx, identity, protection) = Setup();
        ctx.Request.Headers.Authorization = "Bearer ";
        var gate = new HelloContinuationGate<Profile>(_ => throw new InvalidOperationException(), new());
        await gate.InvokeAsync(ctx, identity, new Continuation(), protection);
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

}
