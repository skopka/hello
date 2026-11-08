using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace Skopka.Hello.Admin;

/// <summary>
/// Makes a repeated submission of one step-up confirmation (Enter and a click,
/// a double click, Back and resubmit) end like the first one. The first
/// submission runs; concurrent duplicates wait for it and get its outcome, and
/// a later duplicate within <see cref="Lifetime"/> gets the remembered outcome.
/// The guard is process-local; a duplicate that reaches another instance gets
/// the consumed-challenge result, which the page explains from current state.
/// </summary>
internal sealed class HelloAdminCompletionGuard : IDisposable
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, Lazy<Task<HelloAdminCompletionOutcome>>> running = new(StringComparer.Ordinal);
    private readonly MemoryCache completed = new(new MemoryCacheOptions { SizeLimit = 10_000 });

    public async Task<HelloAdminCompletionOutcome> RunAsync(
        Guid actorUserId,
        Guid challengeId,
        string scope,
        Func<Task<HelloAdminCompletionOutcome>> complete)
    {
        ArgumentNullException.ThrowIfNull(complete);
        var key = $"{actorUserId:N}:{challengeId:N}:{scope}";
        if (completed.TryGetValue(key, out HelloAdminCompletionOutcome? remembered)
            && remembered is not null)
        {
            return remembered;
        }

        var lazy = running.GetOrAdd(
            key,
            _ => new Lazy<Task<HelloAdminCompletionOutcome>>(
                complete,
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            var outcome = await lazy.Value.ConfigureAwait(false);
            if (outcome.Completed)
            {
                completed.Set(key, outcome, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = Lifetime,
                    Size = 1,
                });
            }

            return outcome;
        }
        finally
        {
            running.TryRemove(new KeyValuePair<string, Lazy<Task<HelloAdminCompletionOutcome>>>(key, lazy));
        }
    }

    public void Dispose() => completed.Dispose();
}

/// <summary>
/// What a confirmation submission produced, independent of the request that
/// ran it, so a duplicate request can answer the same way.
/// </summary>
/// <param name="Completed">The mutation committed; duplicates must not report an error.</param>
/// <param name="StatusMessage">One-time message for the page the person returns to.</param>
/// <param name="Errors">Errors to show next to the confirmation when it did not complete.</param>
/// <param name="SignOut">The actor's own session ended; the person returns to Login.</param>
/// <param name="LoginFlag">The Login page flag that explains why the session ended.</param>
/// <param name="KeepPending">The confirmation can be retried with another code.</param>
/// <param name="CheckState">The challenge was consumed or expired; the page explains from current state.</param>
internal sealed record HelloAdminCompletionOutcome(
    bool Completed,
    string? StatusMessage,
    IReadOnlyList<KeyValuePair<string, string>> Errors,
    bool SignOut = false,
    string? LoginFlag = null,
    bool KeepPending = false,
    bool CheckState = false)
{
    /// <summary>The entity the completed action created or changed, when known.</summary>
    public Guid? EntityId { get; init; }

    /// <summary>A one-time state reference (such as a generated secret) the page shows after the redirect.</summary>
    public string? StateToken { get; init; }
}
