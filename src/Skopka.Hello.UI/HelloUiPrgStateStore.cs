using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;

namespace Skopka.Hello.UI;

public sealed class HelloUiPrgStateStore(IMemoryCache cache)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private const string CacheKeyPrefix = "hello-ui-prg:";
    private readonly object sync = new();

    public string Store<T>(T value)
        where T : class
    {
        var token = WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(32));
        lock (sync)
        {
            cache.Set(CacheKeyPrefix + token, value, Lifetime);
        }

        return token;
    }

    public bool TryGet<T>(string? token, out T? value)
        where T : class
    {
        value = null;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        lock (sync)
        {
            return cache.TryGetValue(CacheKeyPrefix + token, out value)
                && value is not null;
        }
    }

    public bool TryTake<T>(string? token, out T? value)
        where T : class
    {
        value = null;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        lock (sync)
        {
            var key = CacheKeyPrefix + token;
            if (!cache.TryGetValue(key, out value) || value is null)
            {
                return false;
            }

            cache.Remove(key);
            return true;
        }
    }

    public void Remove(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        lock (sync)
        {
            cache.Remove(CacheKeyPrefix + token);
        }
    }
}
