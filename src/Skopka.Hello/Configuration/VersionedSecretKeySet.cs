using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace Skopka.Hello;

internal sealed class VersionedSecretKeySet : IDisposable
{
    private readonly Dictionary<string, byte[]> keys;
    private bool disposed;

    private VersionedSecretKeySet(
        string currentVersion,
        Dictionary<string, byte[]> keys)
    {
        CurrentVersion = currentVersion;
        this.keys = keys;
    }

    public string CurrentVersion { get; }

    public IReadOnlyDictionary<string, byte[]> Keys => keys;

    public static VersionedSecretKeySet Load(
        IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(section);

        var currentVersion = section["CurrentVersion"];
        if (string.IsNullOrWhiteSpace(currentVersion))
        {
            throw new InvalidOperationException(
                $"{section.Path}:CurrentVersion is required.");
        }

        var keys = new Dictionary<string, byte[]>(
            StringComparer.Ordinal);
        try
        {
            foreach (var child in section
                .GetSection("Keys")
                .GetChildren())
            {
                if (string.IsNullOrWhiteSpace(child.Value))
                {
                    throw InvalidKey(
                        section,
                        child.Key,
                        "must be a Base64-encoded key");
                }

                byte[] key;
                try
                {
                    key = Convert.FromBase64String(child.Value);
                }
                catch (FormatException exception)
                {
                    throw InvalidKey(
                        section,
                        child.Key,
                        "must be Base64 encoded",
                        exception);
                }

                if (key.Length < 32)
                {
                    CryptographicOperations.ZeroMemory(key);
                    throw InvalidKey(
                        section,
                        child.Key,
                        "must contain at least 32 bytes");
                }

                keys.Add(child.Key, key);
            }

            if (keys.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{section.Path}:Keys must contain at least one versioned key.");
            }

            if (!keys.ContainsKey(currentVersion))
            {
                throw new InvalidOperationException(
                    $"{section.Path}:CurrentVersion must identify a configured key.");
            }

            return new VersionedSecretKeySet(
                currentVersion,
                keys);
        }
        catch
        {
            foreach (var key in keys.Values)
            {
                CryptographicOperations.ZeroMemory(key);
            }

            throw;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (var key in keys.Values)
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static InvalidOperationException InvalidKey(
        IConfigurationSection section,
        string version,
        string requirement,
        Exception? innerException = null)
        => new(
            $"{section.Path}:Keys:{version} {requirement}.",
            innerException);
}
