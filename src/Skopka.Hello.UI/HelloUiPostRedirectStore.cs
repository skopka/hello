using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;

namespace Skopka.Hello.UI;

/// <summary>
/// Holds the rendered outcome of a Hello form submission between the POST and
/// the GET it redirects to. The store is process-local and bounded: when it is
/// full, a new outcome is not kept and the GET renders the page afresh.
/// </summary>
internal sealed class HelloUiPostRedirectStore : IDisposable
{
    /// <summary>How long a submission outcome survives refresh and Back.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>Upper bound for all retained outcomes together, in bytes.</summary>
    public const long SizeLimit = 64L * 1024 * 1024;

    private const int IndexEntrySize = 256;
    private readonly MemoryCache cache = new(new MemoryCacheOptions
    {
        SizeLimit = SizeLimit,
        CompactionPercentage = 0.25,
    });
    private readonly object sync = new();
    private readonly TimeProvider clock;

    public HelloUiPostRedirectStore()
        : this(TimeProvider.System)
    {
    }

    internal HelloUiPostRedirectStore(TimeProvider clock)
        => this.clock = clock;

    /// <summary>
    /// Keeps <paramref name="snapshot"/> as the only outcome of its page for its
    /// browser binding and returns the opaque reference for the redirect URL.
    /// </summary>
    public string? Store(HelloUiPostRedirectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var token = WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(24));
        var expiresAt = clock.GetUtcNow().Add(Lifetime);
        lock (sync)
        {
            RemoveCurrent(snapshot.Binding, snapshot.PageKey);
            cache.Set(
                SnapshotKey(token),
                snapshot,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpiration = expiresAt,
                    Size = snapshot.Size,
                });
            if (!cache.TryGetValue(SnapshotKey(token), out _))
            {
                // The bounded cache refused the entry. The GET then renders
                // the page afresh rather than failing the submission.
                return null;
            }

            cache.Set(
                IndexKey(snapshot.Binding, snapshot.PageKey),
                token,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpiration = expiresAt,
                    Size = IndexEntrySize,
                });
        }

        return token;
    }

    public bool TryGet(
        string? token,
        string binding,
        string pageKey,
        out HelloUiPostRedirectSnapshot? snapshot)
    {
        snapshot = null;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64)
        {
            return false;
        }

        lock (sync)
        {
            if (!cache.TryGetValue(
                    SnapshotKey(token),
                    out HelloUiPostRedirectSnapshot? stored)
                || stored is null
                || !CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(stored.Binding),
                    System.Text.Encoding.UTF8.GetBytes(binding))
                || !string.Equals(
                    stored.PageKey,
                    pageKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            snapshot = stored;
            return true;
        }
    }

    /// <summary>
    /// Forgets the retained outcome of a page for a browser, so Back after a
    /// later submission renders the page afresh instead of an old step.
    /// </summary>
    public void Invalidate(string binding, string pageKey)
    {
        lock (sync)
        {
            RemoveCurrent(binding, pageKey);
        }
    }

    public void Dispose() => cache.Dispose();

    private void RemoveCurrent(string binding, string pageKey)
    {
        var indexKey = IndexKey(binding, pageKey);
        if (cache.TryGetValue(indexKey, out string? previous)
            && previous is not null)
        {
            cache.Remove(SnapshotKey(previous));
        }

        cache.Remove(indexKey);
    }

    private static string SnapshotKey(string token) => "s:" + token;

    private static string IndexKey(string binding, string pageKey)
        => "i:" + binding + ":" + pageKey.ToUpperInvariant();
}

/// <summary>The status, headers and body a Hello page rendered for a POST.</summary>
internal sealed record HelloUiPostRedirectSnapshot(
    string Binding,
    string PageKey,
    int StatusCode,
    IReadOnlyList<KeyValuePair<string, string[]>> Headers,
    byte[] Body)
{
    public long Size => Body.LongLength
        + Headers.Sum(header => header.Key.Length
            + header.Value.Sum(value => value.Length))
        + 512;

    public override string ToString()
        => $"HelloUiPostRedirectSnapshot {{ PageKey = {PageKey}, StatusCode = {StatusCode} }}";
}
