using Microsoft.Extensions.Caching.Memory;
using Skopka.Hello.UI;

namespace Skopka.Hello.Tests;

public sealed class HelloUiPrgStateStoreTests
{
    private static readonly string[] Codes = ["code"];

    [Fact]
    public void StateCanBeReadAcrossGetAndThenConsumed()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new HelloUiPrgStateStore(cache);
        var enrollment = new HelloTotpEnrollment(
            Guid.NewGuid(),
            "secret",
            "otpauth://totp/test",
            "<svg />",
            DateTimeOffset.UtcNow.AddMinutes(5));

        var token = store.Store(enrollment);

        Assert.True(store.TryGet<HelloTotpEnrollment>(
            token,
            out var firstRead));
        Assert.Equal(enrollment, firstRead);
        Assert.True(store.TryTake<HelloTotpEnrollment>(
            token,
            out var taken));
        Assert.Equal(enrollment, taken);
        Assert.False(store.TryGet<HelloTotpEnrollment>(token, out _));
    }

    [Fact]
    public void StateCannotBeReadAsAnotherType()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new HelloUiPrgStateStore(cache);
        var token = store.Store(Codes);

        Assert.False(store.TryGet<HelloTotpEnrollment>(token, out _));
        Assert.True(store.TryTake<string[]>(token, out var codes));
        Assert.NotNull(codes);
        Assert.Equal(Codes, codes);
    }
}
