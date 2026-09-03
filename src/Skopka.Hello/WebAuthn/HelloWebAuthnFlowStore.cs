using Skopka.Identity.RateLimiting;

namespace Skopka.Hello.WebAuthn;

/// <summary>
/// Spends a challenge once.
///
/// A WebAuthn challenge is a single-use value, and this is where that "single"
/// is enforced: the challenge itself travels in a protected payload the server
/// signed, which proves the server issued it and when it expires, but says
/// nothing about whether it has already been answered.
/// </summary>
public interface IHelloWebAuthnFlowStore
{
    Task<bool> TryConsumeAsync(
        Guid flowId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);
}

/// <summary>
/// The persistent guard when the host has a rate limiter, and a bounded
/// process-local one otherwise — the same arrangement the external OIDC flow
/// replay guard uses, and the same warning applies: without a shared limiter a
/// multi-instance host guards each instance separately.
/// </summary>
internal sealed class HelloWebAuthnFlowStore<TProfile> : IHelloWebAuthnFlowStore
{
    private const string Scope = "hello-webauthn-flow";

    private static readonly TimeSpan MaximumChallengeLifetime =
        TimeSpan.FromMinutes(15);

    private readonly IIdentityRateLimiter<TProfile>? rateLimiter;
    private readonly InMemoryHelloWebAuthnFlowStore fallback;

    public HelloWebAuthnFlowStore(
        IEnumerable<IIdentityRateLimiter<TProfile>> rateLimiters,
        InMemoryHelloWebAuthnFlowStore fallback)
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

        // A window of one: the first hit is allowed and every later one is
        // not, which is what spending a value once looks like to a limiter.
        var decision = await rateLimiter.HitAsync(
            new RateLimitRequest(
                Scope,
                flowId.ToString("N"),
                1,
                MaximumChallengeLifetime),
            cancellationToken);
        return decision.IsAllowed;
    }
}

internal sealed class InMemoryHelloWebAuthnFlowStore : IHelloWebAuthnFlowStore
{
    private const int Capacity = 100_000;

    private readonly Lock sync = new();
    private readonly Dictionary<Guid, DateTimeOffset> consumed = [];
    private readonly PriorityQueue<Guid, DateTimeOffset> expirations = new();

    public Task<bool> TryConsumeAsync(
        Guid flowId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        if (flowId == Guid.Empty || expiresAt <= now)
        {
            return Task.FromResult(false);
        }

        lock (sync)
        {
            RemoveExpired(now);
            if (consumed.ContainsKey(flowId) || consumed.Count >= Capacity)
            {
                return Task.FromResult(false);
            }

            consumed.Add(flowId, expiresAt);
            expirations.Enqueue(flowId, expiresAt);
            return Task.FromResult(true);
        }
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        while (expirations.TryPeek(out var flowId, out var expiresAt)
            && expiresAt <= now)
        {
            expirations.Dequeue();
            consumed.Remove(flowId);
        }
    }
}
