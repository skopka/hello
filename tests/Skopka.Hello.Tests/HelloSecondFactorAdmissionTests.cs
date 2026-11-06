using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Skopka.Identity.RateLimiting;
using Skopka.Identity.Sessions;
using Skopka.Identity.Totp;
using Skopka.Identity.Users;

namespace Skopka.Hello.Tests;

public sealed class HelloSecondFactorAdmissionTests
{
    [Fact]
    public async Task NoHostRulePreservesDefaultSessionAdmission()
    {
        var admission = Create([], [], null);
        var result = await admission.CheckAsync(Context(IdentitySessionAssurance.None), CancellationToken.None);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RequiredFactorWithoutRateLimiterFailsClosedWithoutContinuation()
    {
        var admission = Create([new Required()], [], DispatchProxy.Create<IIdentityTotpService<string>, Stub>());
        var result = await admission.CheckAsync(Context(IdentitySessionAssurance.None), CancellationToken.None);
        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Equal(HelloSecondFactorErrorCodes.Required, error.Code);
        Assert.Null(error.Details);
    }

    [Fact]
    public async Task VerifiedAssuranceMeetsHostRule()
    {
        var admission = Create([new Required()], [], null);
        var result = await admission.CheckAsync(Context(IdentitySessionAssurance.TotpVerified), CancellationToken.None);
        Assert.True(result.IsSuccess);
    }

    private static HelloSecondFactorAdmission<string> Create(
        IEnumerable<IHelloSignInSecondFactorPolicy<string>> policies,
        IEnumerable<IIdentityRateLimiter<string>> limiters, IIdentityTotpService<string>? totp)
    {
        var options = new SkopkaHelloOptions();
        options.Totp.Enabled = true;
        return new HelloSecondFactorAdmission<string>(policies,
            new HelloSecondFactorTickets(new EphemeralDataProtectionProvider(), TimeProvider.System),
            new HttpContextAccessor(), new AspNetHelloRequestContext(), options, limiters, totp);
    }
    private static IdentitySessionAdmissionContext<string> Context(IdentitySessionAssurance assurance)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new IdentityUser<string>(Guid.NewGuid(), UserFlags.None, "user", null, false, null, false,
            "profile", 1, "stamp", null, null, null, now, now);
        return new(user, assurance, IdentitySessionAdmissionOperation.Create, null, null);
    }
    private sealed class Required : IHelloSignInSecondFactorPolicy<string>
    {
        public Task<bool> RequiresAsync(IdentityUser<string> user, CancellationToken cancellationToken) => Task.FromResult(true);
    }
    public class Stub : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException();
    }
}
