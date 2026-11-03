using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello;
using Skopka.Hello.WebAuthn;
using Skopka.Identity.Credentials;
using Skopka.Identity.ExternalLogins;
using Skopka.Identity.PhoneOtp;
using Skopka.Identity.RateLimiting;
using Skopka.Identity.Sessions;
using Skopka.Identity.SignInMethods;
using Skopka.Identity.Totp;
using Skopka.Identity.Users;

namespace Skopka.Hello.Tests;

public sealed class HelloTotpEnrollmentAuthorizationTests
{
    [Fact]
    public async Task ProofIsBoundToTokenAndSnapshotAndIsSingleUse()
    {
        var f = new Fixture();
        var proof = await f.ExternalProofAsync();
        Assert.False((await f.AuthorizeAsync("other-token", proof)).IsSuccess);
        f.CurrentId = Guid.NewGuid();
        Assert.False((await f.AuthorizeAsync("token", proof)).IsSuccess);
        f.CurrentId = f.UserId;
        f.Version++;
        Assert.False((await f.AuthorizeAsync("token", proof)).IsSuccess);
        f.Version--;
        Assert.True((await f.AuthorizeAsync("token", proof)).IsSuccess);
        Assert.False((await f.AuthorizeAsync("token", proof)).IsSuccess);
    }

    [Fact]
    public async Task ProofExpiresAndCannotBeUsedAsAnEnrollmentTicket()
    {
        var f = new Fixture();
        var proof = await f.ExternalProofAsync();
        Assert.False(await f.Service.ValidateEnrollmentTicketAsync("token", proof, Guid.NewGuid(), default));
        Assert.False(await f.Service.ValidateEnrollmentTicketAsync("token", "%%%", Guid.NewGuid(), default));
        f.Clock.Now = f.Clock.Now.AddMinutes(6);
        Assert.False((await f.AuthorizeAsync("token", proof)).IsSuccess);
    }

    [Fact]
    public async Task ExternalProofRequiresExactLinkedSubject()
    {
        var f = new Fixture();
        var started = await f.Service.BeginExternalAsync("token", "provider", null, default);
        Assert.True(started.IsSuccess);
        var wrong = await f.Service.CompleteExternalAsync("token", started.Value,
            new ExternalLoginKey("provider", "other-subject"), null, default);
        Assert.False(wrong.IsSuccess);
    }

    [Fact]
    public async Task PasswordCannotBeBypassedByAnEarlierPasswordlessProof()
    {
        var f = new Fixture();
        var proof = await f.ExternalProofAsync();
        f.HasPassword = true;
        Assert.False((await f.AuthorizeAsync("token", proof)).IsSuccess);
    }

    [Fact]
    public async Task RejectedRegisteredValidatorCannotFallBackToSessionService()
    {
        var f = new Fixture { RejectValidator = true };
        var result = await f.Service.BeginExternalAsync("token", "provider", null, default);
        Assert.False(result.IsSuccess);
        Assert.Equal(0, f.SessionValidationCalls);
    }

    [Fact]
    public async Task DisabledTotpDoesNotInvokeAuthenticationOrCredentialServices()
    {
        var f = new Fixture();
        f.Options.Totp.Enabled = false;

        Assert.False((await f.Service.BeginPhoneAsync("token", null, default)).IsSuccess);
        Assert.False((await f.Service.BeginPasskeyAsync("token", null, default)).IsSuccess);
        Assert.False((await f.Service.BeginExternalAsync("token", "provider", null, default)).IsSuccess);
        Assert.False((await f.Service.AuthorizeAsync(new("token", null, "password"), default)).IsSuccess);
        Assert.Equal(0, f.ValidatorCalls);
        Assert.Equal(0, f.SessionValidationCalls);
    }

    [Fact]
    public async Task EnrollmentTicketIsBoundToUserTokenEnrollmentVersionAndExpiry()
    {
        var f = new Fixture();
        var enrollmentId = Guid.NewGuid();
        var ticket = await f.Service.IssueEnrollmentTicketAsync(
            "token", enrollmentId, f.Clock.Now.AddMinutes(10), f.Version, default);
        Assert.NotNull(ticket);
        Assert.Equal(f.Clock.Now.AddMinutes(5), ticket.ExpiresAt);
        Assert.True(await f.Service.ValidateEnrollmentTicketAsync("token", ticket.Ticket, enrollmentId, default));
        Assert.False(await f.Service.ValidateEnrollmentTicketAsync("other", ticket.Ticket, enrollmentId, default));
        Assert.False(await f.Service.ValidateEnrollmentTicketAsync("token", ticket.Ticket, Guid.NewGuid(), default));
        f.CurrentId = Guid.NewGuid();
        Assert.False(await f.Service.ValidateEnrollmentTicketAsync("token", ticket.Ticket, enrollmentId, default));
        f.CurrentId = f.UserId;
        f.Version++;
        Assert.False(await f.Service.ValidateEnrollmentTicketAsync("token", ticket.Ticket, enrollmentId, default));
        f.Version--;
        f.Clock.Now = f.Clock.Now.AddMinutes(6);
        Assert.False(await f.Service.ValidateEnrollmentTicketAsync("token", ticket.Ticket, enrollmentId, default));
    }

    [Fact]
    public async Task PasswordAttemptsAreLimitedPerAccountEvenWhenClientChanges()
    {
        var f = new Fixture { HasPassword = true };
        for (var i = 0; i < 5; i++)
            Assert.True((await f.Service.AuthorizeAsync(new("token", $"client-{i}", "password"), default)).IsSuccess);
        Assert.False((await f.Service.AuthorizeAsync(new("token", "new-client", "password"), default)).IsSuccess);
        f.Clock.Now = f.Clock.Now.AddMinutes(6);
        Assert.True((await f.Service.AuthorizeAsync(new("token", "new-client", "password"), default)).IsSuccess);
    }

    private sealed class Fixture
    {
        public readonly Guid UserId = Guid.NewGuid();
        public Guid CurrentId;
        private readonly ExternalLoginKey login = new("provider", "subject");
        public readonly MutableTime Clock = new();
        public long Version = 1;
        public bool HasPassword;
        public bool RejectValidator;
        public int ValidatorCalls;
        public int SessionValidationCalls;
        public SkopkaHelloOptions Options { get; } = new();
        public HelloTotpEnrollmentAuthorization<string> Service { get; }

        public Fixture()
        {
            CurrentId = UserId;
            Options.Totp.Enabled = true;
            var user = new IdentityUser<string>(UserId, UserFlags.None, "user", null, false, null, false,
                "profile", 1, "stamp", null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            var validator = Proxy<IHelloAccessTokenValidator<string>>(_ =>
            {
                ValidatorCalls++;
                return Task.FromResult(RejectValidator
                    ? OperationResultFactory.Fail<IdentityUser<string>>(new Error("test.invalid", "Invalid.", ErrorType.Unauthorized))
                    : OperationResultFactory.Success(user with { Id = CurrentId }));
            });
            var methods = Proxy<IIdentitySignInMethodQueryService<string>>(_ => Task.FromResult(OperationResultFactory.Success(
                new SignInMethodSnapshot(CurrentId, Version, HasPassword, [new ExternalLoginInfo(CurrentId, login, Clock.Now)]))));
            var passwords = Proxy<IPasswordCredentialService<string>>(_ => Task.FromResult(OperationResultFactory.Success()));
            Service = new HelloTotpEnrollmentAuthorization<string>(
                new EphemeralDataProtectionProvider(), new FlowStore(), Proxy<IIdentitySessionService<string>>(_ =>
                {
                    SessionValidationCalls++;
                    return Task.FromResult(OperationResultFactory.Success(user));
                }),
                methods, passwords, Proxy<IHelloPhoneOtpApplication<string>>(_ => throw new InvalidOperationException()),
                Proxy<IIdentityPhoneOtpService<string>>(_ => throw new InvalidOperationException()), Options,
                [validator], Array.Empty<IIdentityRateLimiter<string>>(), new HelloTotpPasswordAttempts(Clock), Clock,
                totp: Proxy<IIdentityTotpService<string>>(_ => throw new InvalidOperationException()));
        }

        public async Task<string> ExternalProofAsync()
        {
            var started = await Service.BeginExternalAsync("token", login.Provider, null, default);
            Assert.True(started.IsSuccess);
            var completed = await Service.CompleteExternalAsync("token", started.Value, login, null, default);
            Assert.True(completed.IsSuccess);
            return completed.Value;
        }

        public Task<OperationResult<long>> AuthorizeAsync(string token, string proof)
            => Service.AuthorizeAsync(new(token, null, Proof: proof), default);
    }

    private sealed class MutableTime : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FlowStore : IHelloWebAuthnFlowStore
    {
        private readonly HashSet<Guid> consumed = [];
        public Task<bool> TryConsumeAsync(Guid id, DateTimeOffset expiresAt, CancellationToken cancellationToken)
            => Task.FromResult(consumed.Add(id));
    }

    private static T Proxy<T>(Func<object?[]?, object?> invoke) where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).Handler = invoke;
        return proxy;
    }

    public class TestProxy : DispatchProxy
    {
        public Func<object?[]?, object?> Handler { get; set; } = _ => throw new InvalidOperationException();
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(args);
    }
}
