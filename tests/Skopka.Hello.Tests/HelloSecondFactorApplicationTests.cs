using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.WebAuthn;
using Skopka.Identity.Authentication;
using Skopka.Identity.Sessions;
using Skopka.Identity.Users;
using Skopka.Identity.Verification;

namespace Skopka.Hello.Tests;

public sealed class HelloSecondFactorApplicationTests
{
    [Fact]
    public async Task InvalidTamperedAndExpiredTicketsCannotIssueSession()
    {
        var fixture = new Fixture();
        var ticket = fixture.Tickets.New(fixture.User.Id, fixture.User.SecurityStamp, null, null);
        var valid = fixture.Tickets.Seal(ticket);
        var expired = fixture.Tickets.Seal(ticket with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1), Stage = "ack" });
        var tampered = valid[..20] + (valid[20] == 'A' ? "B" : "A") + valid[21..];
        Assert.False((await fixture.App.AcknowledgeEnrollmentAsync("invalid", CancellationToken.None)).IsSuccess);
        Assert.False((await fixture.App.AcknowledgeEnrollmentAsync(tampered, CancellationToken.None)).IsSuccess);
        Assert.False((await fixture.App.AcknowledgeEnrollmentAsync(expired, CancellationToken.None)).IsSuccess);
        Assert.Equal(0, fixture.Issued);
    }

    [Fact]
    public async Task ChangedStampAfterEnrollmentCannotIssueSession()
    {
        var fixture = new Fixture();
        var ticket = fixture.Acknowledgment();
        fixture.User = fixture.User with { SecurityStamp = "new-stamp" };
        Assert.False((await fixture.App.AcknowledgeEnrollmentAsync(ticket, CancellationToken.None)).IsSuccess);
        Assert.Equal(0, fixture.Issued);
    }

    [Fact]
    public async Task AcknowledgmentIsSingleUseAndCreatesOnlyOneVerifiedSession()
    {
        var fixture = new Fixture();
        var ticket = fixture.Acknowledgment();
        var outcomes = await Task.WhenAll(
            fixture.App.AcknowledgeEnrollmentAsync(ticket, CancellationToken.None),
            fixture.App.AcknowledgeEnrollmentAsync(ticket, CancellationToken.None));
        Assert.Single(outcomes, result => result.IsSuccess);
        Assert.Equal(1, fixture.Issued);
        Assert.Equal(IdentitySessionAssurance.TotpVerified, fixture.Assurance);
    }

    [Fact]
    public async Task IncorrectCodeDoesNotConsumeContinuationOrIssueSession()
    {
        var fixture = new Fixture();
        var state = fixture.Tickets.New(fixture.User.Id, fixture.User.SecurityStamp, null, null) with
        {
            Stage = "verify",
            ChallengeId = Guid.NewGuid(),
        };
        var ticket = fixture.Tickets.Seal(state);
        Assert.False((await fixture.App.CompleteVerificationAsync(ticket, "wrong", null, CancellationToken.None)).IsSuccess);
        Assert.False((await fixture.App.CompleteVerificationAsync(ticket, "wrong", null, CancellationToken.None)).IsSuccess);
        Assert.Equal(2, fixture.VerifyAttempts);
        Assert.Equal(0, fixture.Issued);
    }

    private sealed class Fixture
    {
        public IdentityUser<string> User { get; set; }
        public HelloSecondFactorTickets Tickets { get; }
        public HelloSecondFactorApplication<string> App { get; }
        public int Issued => issued;
        public int VerifyAttempts { get; private set; }
        public IdentitySessionAssurance Assurance { get; private set; }
        public Fixture()
        {
            var now = DateTimeOffset.UtcNow;
            User = new IdentityUser<string>(Guid.NewGuid(), UserFlags.None, "user", null, false, null, false,
                "profile", 1, "stamp", null, null, null, now, now);
            Tickets = new HelloSecondFactorTickets(new EphemeralDataProtectionProvider(), TimeProvider.System);
            var lookup = Proxy<IIdentityUserLookupService<string>>((method, args) =>
                method.Name == nameof(IIdentityUserLookupService<string>.FindActiveByIdAsync)
                    ? Task.FromResult(OperationResultFactory.Success(User)) : throw new NotSupportedException());
            var sessions = Proxy<IIdentitySessionService<string>>((method, args) =>
            {
                if (method.Name == nameof(IIdentitySessionService<string>.CreateAsync))
                {
                    var command = (CreateIdentitySessionCommand)args![0]!;
                    Assurance = command.Assurance;
                    Interlocked.Increment(ref issued);
                    return Task.FromResult(OperationResultFactory.Success(new IssuedIdentitySession(Guid.NewGuid(), "access", now.AddMinutes(5), "refresh", now.AddDays(1))));
                }
                if (method.Name == nameof(IIdentitySessionService<string>.ValidateAccessTokenAsync))
                    return Task.FromResult(OperationResultFactory.Success(User));
                throw new NotSupportedException();
            });
            App = new HelloSecondFactorApplication<string>(Tickets, new InMemoryHelloWebAuthnFlowStore(),
                Proxy<IIdentityVerificationService<string>>((method, _) =>
                {
                    if (method.Name != nameof(IIdentityVerificationService<string>.VerifyAsync)) throw new NotSupportedException();
                    VerifyAttempts++;
                    return Task.FromResult(OperationResultFactory.Fail<VerificationProof>(new Error("invalid", "invalid", ErrorType.Unauthorized)));
                }),
                sessions, lookup, new SkopkaHelloOptions());
        }
        private int issued;
        public string Acknowledgment() => Tickets.Seal(Tickets.New(User.Id, User.SecurityStamp, null, null) with { Stage = "ack" });
        private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> callback) where T : class
        {
            var proxy = DispatchProxy.Create<T, Stub>();
            ((Stub)(object)proxy).Callback = callback;
            return proxy;
        }
    }
    public class Stub : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Callback { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Callback(targetMethod!, args);
    }
}

