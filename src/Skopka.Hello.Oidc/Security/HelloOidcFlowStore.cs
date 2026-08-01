using Skopka.Identity.RateLimiting;

namespace Skopka.Hello.Oidc;

internal sealed class HelloOidcFlowStore<TProfile>
    : IHelloOidcFlowStore
{
    private const string Scope = "hello-oidc-flow";
    private static readonly TimeSpan MaximumFlowLifetime =
        TimeSpan.FromMinutes(30);

    private readonly IIdentityRateLimiter<TProfile>? rateLimiter;
    private readonly InMemoryHelloOidcFlowStore fallback;

    public HelloOidcFlowStore(
        IEnumerable<IIdentityRateLimiter<TProfile>> rateLimiters,
        InMemoryHelloOidcFlowStore fallback)
    {
        ArgumentNullException.ThrowIfNull(rateLimiters);
        ArgumentNullException.ThrowIfNull(fallback);

        rateLimiter = rateLimiters.FirstOrDefault();
        this.fallback = fallback;
    }

    public async Task<bool> TryConsumeAsync(
        Guid flowId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (flowId == Guid.Empty || expiresAt <= DateTimeOffset.UtcNow)
        {
            return false;
        }

        if (rateLimiter is null)
        {
            return await fallback.TryConsumeAsync(
                flowId,
                expiresAt,
                cancellationToken);
        }

        var decision = await rateLimiter.HitAsync(
            new RateLimitRequest(
                Scope,
                flowId.ToString("N"),
                1,
                MaximumFlowLifetime),
            cancellationToken);
        return decision.IsAllowed;
    }
}
