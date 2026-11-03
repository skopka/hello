using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Playwright;
using Skopka.Hello.IntegrationTests;

namespace Skopka.Hello.BrowserTests;

public sealed class TotpEnrollmentBrowserTests(BrowserFixture browser) : IClassFixture<BrowserFixture>
{
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public async Task EnrollmentRequiresFreshPasswordAndDoesNotPersistIt(string culture)
    {
        await using var host = await AuthenticationFlowTests.AdminBrowserHost.CreateAsync();
        await using var context = await browser.Browser.NewContextAsync(new() { Locale = culture });
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(host.Origin, "/hello/login").ToString());
        await page.Locator("[name='Input.Login']").FillAsync("browser-admin@example.test");
        await page.Locator("[name='Input.Password']").FillAsync("correct horse battery staple");
        await page.Locator("form:has([name='Input.Password']) button[type='submit']").ClickAsync();
        await page.GotoAsync(new Uri(host.Origin, "/hello/account/security").ToString());
        var form = page.Locator("form:has(#totp-password)");
        await Assertions.Expect(form).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".hello-qr-code")).ToHaveCountAsync(0);
        var csrfStatus = await page.EvaluateAsync<int>("async () => (await fetch('?handler=BeginTotp', {method:'POST', headers:{'Content-Type':'application/x-www-form-urlencoded'}, body:'password=wrong'})).status");
        Assert.Equal(400, csrfStatus);
        await page.Locator("#totp-password").FillAsync("wrong password");
        await form.Locator("button[type='submit']").ClickAsync();
        await Assertions.Expect(page.Locator(".hello-qr-code")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#totp-password")).ToHaveValueAsync("");
        await page.Locator("#totp-password").FillAsync("correct horse battery staple");
        await page.Locator("form:has(#totp-password) button[type='submit']").ClickAsync();
        await Assertions.Expect(page.Locator(".hello-qr-code svg")).ToBeVisibleAsync();
        var secret = await page.Locator(".hello-qr-code + p code").InnerTextAsync();
        await page.Locator("#totp-enrollment-code").FillAsync("invalid");
        await page.Locator("form:has(#totp-enrollment-code) button[type='submit']").ClickAsync();
        var codeInput = page.Locator("#totp-enrollment-code, #totp-retry-code");
        await Assertions.Expect(codeInput).ToBeVisibleAsync();
        Assert.DoesNotContain("correct horse battery staple", await page.ContentAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain("correct horse battery staple", await page.EvaluateAsync<string>("() => JSON.stringify([localStorage, sessionStorage, document.cookie])"), StringComparison.Ordinal);
        await codeInput.FillAsync(ComputeTotp(secret));
        await codeInput.Locator("xpath=ancestor::form").Locator("button[type='submit']").ClickAsync();
        await Assertions.Expect(page.Locator(".hello-qr-code")).ToHaveCountAsync(0);
        Assert.True(await page.Locator(".hello-recovery-codes li").CountAsync() > 0);
        await Assertions.Expect(page.Locator("form[action*='BeginDisableTotp']")).ToBeVisibleAsync();
    }

    private static string ComputeTotp(string secret)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var accumulator = 0;
        var key = new List<byte>();
        foreach (var character in secret.TrimEnd('=').ToUpperInvariant())
        {
            accumulator = (accumulator << 5) | alphabet.IndexOf(character);
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                key.Add((byte)(accumulator >> bits));
            }
        }
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        // The test must match the interoperable RFC 6238 HMAC-SHA1 profile.
#pragma warning disable CA5350
        var hash = HMACSHA1.HashData(key.ToArray(), counter);
#pragma warning restore CA5350
        var offset = hash[^1] & 15;
        return ((BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & int.MaxValue) % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }
}
