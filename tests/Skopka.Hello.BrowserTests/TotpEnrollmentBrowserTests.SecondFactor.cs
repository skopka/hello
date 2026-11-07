using Microsoft.Playwright;
using Skopka.Hello.IntegrationTests;

namespace Skopka.Hello.BrowserTests;

public sealed partial class TotpEnrollmentBrowserTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public async Task RequiredSecondFactorIsImmediatelyActionableAndSurvivesReload(string culture)
    {
        await using var host = await AuthenticationFlowTests.AdminBrowserHost.CreateAsync(requireSecondFactor: true);
        await using var context = await browser.Browser.NewContextAsync(new() { Locale = culture });
        var page = await context.NewPageAsync();
        await SignInWithPasswordAsync(page, host.Origin);

        var codeInput = page.Locator("[name='SecondFactorCode']");
        var qr = page.Locator(".hello-qr-code img");
        await Assertions.Expect(codeInput).ToBeVisibleAsync();
        await Assertions.Expect(qr).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("form[action*='SecondFactorStart']")).ToHaveCountAsync(0);
        var secret = await page.Locator(".hello-qr-code ~ code").InnerTextAsync();
        Assert.NotEmpty(secret);
        Assert.DoesNotContain(await context.CookiesAsync(), cookie => cookie.Name.Contains("refresh", StringComparison.OrdinalIgnoreCase));
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"));

        await page.ReloadAsync();
        await Assertions.Expect(codeInput).ToBeVisibleAsync();
        await Assertions.Expect(qr).ToBeVisibleAsync();
        Assert.Equal(secret, await page.Locator(".hello-qr-code ~ code").InnerTextAsync());
        var currentCode = ComputeTotp(secret);
        await codeInput.FillAsync(currentCode == "000000" ? "111111" : "000000");
        await page.Locator("form[action*='SecondFactorVerify'] button").ClickAsync();
        await Assertions.Expect(codeInput).ToBeVisibleAsync();
        await Assertions.Expect(qr).ToBeVisibleAsync();
        Assert.Equal(secret, await page.Locator(".hello-qr-code ~ code").InnerTextAsync());
        await page.ReloadAsync();
        await Assertions.Expect(qr).ToBeVisibleAsync();
        Assert.Equal(secret, await page.Locator(".hello-qr-code ~ code").InnerTextAsync());
        await codeInput.FillAsync(ComputeTotp(secret));
        await page.Locator("form[action*='SecondFactorVerify'] button").ClickAsync();
        await Assertions.Expect(page.Locator("form[action*='SecondFactorAcknowledge']")).ToBeVisibleAsync();
        Assert.DoesNotContain(await context.CookiesAsync(), cookie => cookie.Name.Contains("refresh", StringComparison.OrdinalIgnoreCase));
        var recoveryCode = await page.Locator("form[action*='SecondFactorAcknowledge']").Locator("xpath=preceding-sibling::ul[1]/li[1]/code").InnerTextAsync();
        await page.Locator("form[action*='SecondFactorAcknowledge'] button").ClickAsync();
        Assert.Contains(await context.CookiesAsync(), cookie => cookie.Name.Contains("refresh", StringComparison.OrdinalIgnoreCase));

        await using var nextContext = await browser.Browser.NewContextAsync(new() { Locale = culture });
        var nextPage = await nextContext.NewPageAsync();
        await SignInWithPasswordAsync(nextPage, host.Origin);
        await Assertions.Expect(nextPage.Locator("[name='SecondFactorCode']")).ToBeVisibleAsync();
        await Assertions.Expect(nextPage.Locator(".hello-qr-code")).ToHaveCountAsync(0);
        await Assertions.Expect(nextPage.GetByText(culture == "ru"
            ? "Если у вас нет ни приложения-аутентификатора, ни резервных кодов, обратитесь к администратору."
            : "If you have neither your authenticator app nor recovery codes, contact your administrator.")).ToBeVisibleAsync();
        await nextPage.ReloadAsync();
        await Assertions.Expect(nextPage.Locator("[name='SecondFactorCode']")).ToBeVisibleAsync();
        await nextPage.Locator("form[action*='SecondFactorRestart'] button").ClickAsync();
        await Assertions.Expect(nextPage.Locator("[name='Input.Login']")).ToBeVisibleAsync();
        await SignInWithPasswordAsync(nextPage, host.Origin);
        await nextPage.Locator("[name='SecondFactorCode']").FillAsync(recoveryCode);
        await nextPage.Locator("form[action*='SecondFactorVerify'] button").ClickAsync();
        Assert.Contains(await nextContext.CookiesAsync(), cookie => cookie.Name.Contains("refresh", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task SignInWithPasswordAsync(IPage page, Uri origin)
    {
        await page.GotoAsync(new Uri(origin, "/hello/login").ToString());
        await page.Locator("[name='Input.Login']").FillAsync("browser-admin@example.test");
        await page.Locator("[name='Input.Password']").FillAsync("correct horse battery staple");
        await page.Locator("form:has([name='Input.Password']) button[type='submit']").ClickAsync();
    }
}
