using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;
using Skopka.Identity.RateLimiting;
using Skopka.Identity.Sessions;

namespace Skopka.Hello.Tests;

public sealed class HelloRegistrationAdmissionTests
{
    [Fact]
    public async Task HostPolicyRunsBeforePersistentLimits()
    {
        var limiter = new RecordingRateLimiter();
        var policy = new RejectingPolicy();
        var admission = CreateAdmission([policy], limiter);

        var result = await admission.CheckAsync(
            HelloRegistrationKind.External,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "test.registration.denied",
            Assert.Single(result.Errors).Code);
        Assert.Empty(limiter.Hits);
        Assert.Equal(HelloRegistrationKind.External, policy.Last?.Kind);
        Assert.Equal("client-42", policy.Last?.ClientKey);
    }

    [Fact]
    public async Task PersistentLimitsCoverClientAndGlobalAdmission()
    {
        var limiter = new RecordingRateLimiter();
        var admission = CreateAdmission([], limiter);

        var result = await admission.CheckAsync(
            HelloRegistrationKind.Password,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Collection(
            limiter.Hits,
            client =>
            {
                Assert.Equal("hello.registration.client", client.Scope);
                Assert.Equal("client-42", client.Key);
                Assert.Equal(5, client.PermitLimit);
                Assert.Equal(TimeSpan.FromHours(1), client.Window);
            },
            global =>
            {
                Assert.Equal("hello.registration.global", global.Scope);
                Assert.Equal("all", global.Key);
                Assert.Equal(100, global.PermitLimit);
                Assert.Equal(TimeSpan.FromMinutes(1), global.Window);
            });
    }

    [Fact]
    public async Task RejectedPersistentLimitReturnsRateLimitError()
    {
        var retryAfter = DateTimeOffset.UtcNow.AddMinutes(1);
        var limiter = new RecordingRateLimiter(
            new RateLimitDecision(false, retryAfter));
        var admission = CreateAdmission([], limiter);

        var result = await admission.CheckAsync(
            HelloRegistrationKind.Password,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Equal(IdentityErrorCodes.RateLimitExceeded, error.Code);
        Assert.Equal(
            retryAfter,
            Assert.IsType<RateLimitDetails>(error.Details).RetryAfter);
        Assert.Single(limiter.Hits);
    }

    private static HelloRegistrationAdmission<object> CreateAdmission(
        IReadOnlyList<IHelloRegistrationAdmissionPolicy> policies,
        IIdentityRateLimiter<object> limiter)
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext(),
        };
        return new HelloRegistrationAdmission<object>(
            policies,
            [limiter],
            accessor,
            new FixedRequestContext(),
            new SkopkaHelloOptions());
    }

    private sealed class FixedRequestContext : IHelloRequestContext
    {
        public string? CreateClientKey(HttpContext httpContext)
            => "client-42";

        public IdentitySessionMetadata CreateSessionMetadata(
            HttpContext httpContext,
            string clientName)
            => throw new NotSupportedException();
    }

    private sealed class RejectingPolicy
        : IHelloRegistrationAdmissionPolicy
    {
        public HelloRegistrationAdmissionContext? Last { get; private set; }

        public Task<OperationResult> CheckAsync(
            HelloRegistrationAdmissionContext context,
            CancellationToken cancellationToken)
        {
            Last = context;
            return Task.FromResult(
                OperationResultFactory.Fail(
                    new Error(
                        "test.registration.denied",
                        "Registration denied.",
                        ErrorType.Forbidden)));
        }
    }

    private sealed class RecordingRateLimiter(
        params RateLimitDecision[] decisions)
        : IIdentityRateLimiter<object>
    {
        private int decisionIndex;

        public List<RateLimitRequest> Hits { get; } = [];

        public Task<RateLimitDecision> CheckAsync(
            RateLimitRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<RateLimitDecision> HitAsync(
            RateLimitRequest request,
            CancellationToken cancellationToken)
        {
            Hits.Add(request);
            var decision = decisionIndex < decisions.Length
                ? decisions[decisionIndex++]
                : new RateLimitDecision(true, null);
            return Task.FromResult(decision);
        }

        public Task ResetAsync(
            string scope,
            string key,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<int> PruneAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
