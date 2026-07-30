using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace Skopka.Hello.Tests;

public sealed class VersionedSecretKeySetTests
{
    [Fact]
    public void LoadReadsVersionedKeysAndDisposeClearsThem()
    {
        var current = RandomNumberGenerator.GetBytes(32);
        var previous = RandomNumberGenerator.GetBytes(32);
        var configuration = Configuration(
            new Dictionary<string, string?>
            {
                ["CurrentVersion"] = "v2",
                ["Keys:v1"] = Convert.ToBase64String(previous),
                ["Keys:v2"] = Convert.ToBase64String(current),
            });

        var keySet = VersionedSecretKeySet.Load(configuration);
        var currentCopy = keySet.Keys["v2"];
        var previousCopy = keySet.Keys["v1"];

        Assert.Equal("v2", keySet.CurrentVersion);
        Assert.Equal(current, currentCopy);
        Assert.Equal(previous, previousCopy);

        keySet.Dispose();

        Assert.All(currentCopy, value => Assert.Equal(0, value));
        Assert.All(previousCopy, value => Assert.Equal(0, value));
    }

    [Fact]
    public void LoadRequiresCurrentVersionToHaveAKey()
    {
        var configuration = Configuration(
            new Dictionary<string, string?>
            {
                ["CurrentVersion"] = "v2",
                ["Keys:v1"] = Convert.ToBase64String(
                    RandomNumberGenerator.GetBytes(32)),
            });

        var exception = Assert.Throws<InvalidOperationException>(
            () => VersionedSecretKeySet.Load(configuration));

        Assert.Contains(
            "CurrentVersion must identify a configured key",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LoadRejectsMalformedOrShortKeys()
    {
        var malformed = Configuration(
            new Dictionary<string, string?>
            {
                ["CurrentVersion"] = "v1",
                ["Keys:v1"] = "not-base64",
            });
        var shortKeyConfiguration = Configuration(
            new Dictionary<string, string?>
            {
                ["CurrentVersion"] = "v1",
                ["Keys:v1"] = Convert.ToBase64String(
                    RandomNumberGenerator.GetBytes(31)),
            });

        Assert.Throws<InvalidOperationException>(
            () => VersionedSecretKeySet.Load(malformed));
        Assert.Throws<InvalidOperationException>(
            () => VersionedSecretKeySet.Load(
                shortKeyConfiguration));
    }

    private static IConfigurationSection Configuration(
        IReadOnlyDictionary<string, string?> values)
    {
        var prefixed = values.ToDictionary(
            entry => $"SkopkaHello:Secrets:{entry.Key}",
            entry => entry.Value,
            StringComparer.Ordinal);
        return new ConfigurationBuilder()
            .AddInMemoryCollection(prefixed)
            .Build()
            .GetSection("SkopkaHello:Secrets");
    }
}
