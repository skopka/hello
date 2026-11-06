using Microsoft.Playwright;
using Skopka.Hello.IntegrationTests;

namespace Skopka.Hello.BrowserTests;

public sealed partial class TotpEnrollmentBrowserTests
{
    [Fact]
    public async Task RequiredEnrollmentShowsQrAndDoesNotAuthenticateUntilRecoveryAcknowledged()
    {
        await using var host = await AuthenticationFlowTests.AdminBrowserHost.CreateAsync(requireSecondFactor: true);
        await using var context = await browser.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(host.Origin, "/hello/login").ToString());
        await page.Locator("[name='Input.Login']").FillAsync("browser-admin@example.test");
        await page.Locator("[name='Input.Password']").FillAsync("correct horse battery staple");
        await page.Locator("form:has([name='Input.Password']) button[type='submit']").ClickAsync();
        await Assertions.Expect(page.Locator("form[action*='SecondFactorStart']")).ToBeVisibleAsync();
        Assert.DoesNotContain(await context.CookiesAsync(), cookie => cookie.Name.Contains("refresh", StringComparison.OrdinalIgnoreCase));
        await page.Locator("form[action*='SecondFactorStart'] button").ClickAsync();
        await Assertions.Expect(page.Locator(".hello-qr-code img")).ToBeVisibleAsync();
        var secret = await page.Locator(".hello-qr-code ~ code").InnerTextAsync();
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"));
        await page.Locator("[name='SecondFactorCode']").FillAsync(ComputeTotp(secret));
        await page.Locator("form[action*='SecondFactorVerify'] button").ClickAsync();
        await Assertions.Expect(page.Locator("form[action*='SecondFactorAcknowledge']")).ToBeVisibleAsync();
        Assert.DoesNotContain(await context.CookiesAsync(), cookie => cookie.Name.Contains("refresh", StringComparison.OrdinalIgnoreCase));
        await page.Locator("form[action*='SecondFactorAcknowledge'] button").ClickAsync();
        Assert.Contains(await context.CookiesAsync(), cookie => cookie.Name.Contains("refresh", StringComparison.OrdinalIgnoreCase));
    }
}
