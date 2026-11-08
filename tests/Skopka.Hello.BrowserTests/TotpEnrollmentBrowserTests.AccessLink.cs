using System.Globalization;
using Microsoft.Playwright;
using Skopka.Hello.IntegrationTests;

namespace Skopka.Hello.BrowserTests;

public sealed partial class TotpEnrollmentBrowserTests
{
    [Fact]
    public async Task AccessLinkReloadsPreserveEnrollmentAndShowRecoveryCodesOnlyOnce()
    {
        await using var host = await AuthenticationFlowTests.AdminBrowserHost.CreateAsync(accessLinksEnabled: true);
        var link = await host.IssueTargetAccessLinkAsync();
        await using var context = await browser.Browser.NewContextAsync(new() { Locale = "ru-RU" });
        var page = await context.NewPageAsync();
        var requests = new List<(string Method, string Url)>();
        page.Request += (_, request) => requests.Add((request.Method, request.Url));
        const string password = "another strong password 123";
        await page.GotoAsync(link.ToString());
        await Assertions.Expect(page.Locator("form[action*='handler=Begin']")).ToBeVisibleAsync();
        Assert.EndsWith("/hello/access-link", page.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("token=", page.Url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(requests, request => request.Url.Contains("token=", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(requests, request => request.Method == "POST" && request.Url.Contains("handler=Inspect", StringComparison.OrdinalIgnoreCase));
        await page.Locator("form[action*='handler=Begin'] button").ClickAsync();
        var qr = page.Locator(".hello-qr-code img");
        await Assertions.Expect(qr).ToBeVisibleAsync();
        Assert.EndsWith("/hello/access-link", page.Url, StringComparison.Ordinal);
        var secret = await page.Locator(".hello-qr-code + p code").InnerTextAsync();
        var qrSource = await qr.GetAttributeAsync("src");
        var previewDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/access-link-ux-preview"));
        Directory.CreateDirectory(previewDirectory);
        await page.ScreenshotAsync(new()
        {
            Path = Path.Combine(previewDirectory, "desktop.png"),
            FullPage = true,
            Mask = [page.Locator(".hello-qr-code"), page.Locator(".hello-qr-code + p")],
        });
        await page.SetViewportSizeAsync(375, 812);
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"));
        await page.ScreenshotAsync(new()
        {
            Path = Path.Combine(previewDirectory, "mobile.png"),
            FullPage = true,
            Mask = [page.Locator(".hello-qr-code"), page.Locator(".hello-qr-code + p")],
        });
        await page.SetViewportSizeAsync(1280, 720);
        var beginCount = requests.Count(request => request.Method == "POST" && request.Url.Contains("handler=Begin", StringComparison.OrdinalIgnoreCase));
        await page.ReloadAsync();
        await Assertions.Expect(qr).ToHaveAttributeAsync("src", qrSource!);
        Assert.Equal(secret, await page.Locator(".hello-qr-code + p code").InnerTextAsync());
        Assert.Equal(beginCount, requests.Count(request => request.Method == "POST" && request.Url.Contains("handler=Begin", StringComparison.OrdinalIgnoreCase)));
        var correct = ComputeTotp(secret);
        var wrong = ((int.Parse(correct, CultureInfo.InvariantCulture) + 1) % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        await page.Locator("input[name='Password']").FillAsync(password);
        await page.Locator("input[name='ConfirmPassword']").FillAsync(password);
        await page.Locator("input[name='Code']").FillAsync(wrong);
        await page.Locator("form[action*='handler=Complete'] button").ClickAsync();
        await Assertions.Expect(page.Locator(".hello-validation")).ToContainTextAsync("Не удалось завершить настройку. Проверьте текущий код аутентификатора");
        Assert.EndsWith("/hello/access-link", page.Url, StringComparison.Ordinal);
        await Assertions.Expect(page.Locator("input[name='Password']")).ToHaveValueAsync("");
        await Assertions.Expect(page.Locator("input[name='Code']")).ToHaveValueAsync("");
        Assert.DoesNotContain(password, await page.ContentAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(wrong, await page.ContentAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(password, await page.EvaluateAsync<string>("() => JSON.stringify([localStorage, sessionStorage, document.cookie])"), StringComparison.Ordinal);
        var completeCount = requests.Count(request => request.Method == "POST" && request.Url.Contains("handler=Complete", StringComparison.OrdinalIgnoreCase));
        await page.ReloadAsync();
        await Assertions.Expect(qr).ToHaveAttributeAsync("src", qrSource!);
        Assert.Equal(completeCount, requests.Count(request => request.Method == "POST" && request.Url.Contains("handler=Complete", StringComparison.OrdinalIgnoreCase)));
        await page.Locator("input[name='Password']").FillAsync(password);
        await page.Locator("input[name='ConfirmPassword']").FillAsync(password);
        await page.Locator("input[name='Code']").FillAsync(ComputeTotp(secret));
        await page.Locator("form[action*='handler=Complete'] button").ClickAsync();
        await Assertions.Expect(page.Locator(".hello-recovery-codes li").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[role='status']")).ToContainTextAsync("Доступ к аккаунту настроен.");
        Assert.EndsWith("/hello/access-link", page.Url, StringComparison.Ordinal);
        Assert.DoesNotContain(password, await page.ContentAsync(), StringComparison.Ordinal);
        var postCount = requests.Count(request => request.Method == "POST");
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator(".hello-recovery-codes li")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("[role='status']")).ToContainTextAsync("Доступ к аккаунту настроен.");
        Assert.Equal(postCount, requests.Count(request => request.Method == "POST"));
        Assert.DoesNotContain(requests, request => request.Url.Contains("token=", StringComparison.OrdinalIgnoreCase));
        await page.GotoAsync(link.ToString());
        await Assertions.Expect(page.Locator(".hello-validation")).ToContainTextAsync("Ссылка недействительна, истекла или уже использована.");
        await Assertions.Expect(page.Locator("form[action*='handler=Complete']")).ToHaveCountAsync(0);
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator(".hello-validation")).ToContainTextAsync("Ссылка недействительна, истекла или уже использована.");
    }

    [Fact]
    public async Task AccessLinkCanRestartAfterEnrollmentIsSupersededInAnotherTab()
    {
        await using var host = await AuthenticationFlowTests.AdminBrowserHost.CreateAsync(accessLinksEnabled: true);
        var link = await host.IssueTargetAccessLinkAsync();
        await using var context = await browser.Browser.NewContextAsync(new() { Locale = "ru-RU" });
        var first = await context.NewPageAsync();
        await first.GotoAsync(link.ToString());
        await first.Locator("form[action*='handler=Begin'] button").ClickAsync();
        var oldSecret = await first.Locator(".hello-qr-code + p code").InnerTextAsync();
        var second = await context.NewPageAsync();
        await second.GotoAsync(link.ToString());
        await second.Locator("form[action*='handler=Begin'] button").ClickAsync();
        await Assertions.Expect(second.Locator(".hello-qr-code img")).ToBeVisibleAsync();
        await first.Locator("input[name='Password']").FillAsync("another strong password 123");
        await first.Locator("input[name='ConfirmPassword']").FillAsync("another strong password 123");
        await first.Locator("input[name='Code']").FillAsync(ComputeTotp(oldSecret));
        await first.Locator("form[action*='handler=Complete'] button").ClickAsync();
        await Assertions.Expect(first.Locator(".hello-validation")).ToContainTextAsync("Не удалось завершить настройку. Проверьте текущий код аутентификатора");
        await first.GetByRole(AriaRole.Button, new() { Name = "Начать настройку заново" }).ClickAsync();
        var newSecret = await first.Locator(".hello-qr-code + p code").InnerTextAsync();
        Assert.NotEqual(oldSecret, newSecret);
        await first.Locator("input[name='Password']").FillAsync("another strong password 123");
        await first.Locator("input[name='ConfirmPassword']").FillAsync("another strong password 123");
        await first.Locator("input[name='Code']").FillAsync(ComputeTotp(newSecret));
        await first.Locator("form[action*='handler=Complete'] button").ClickAsync();
        await Assertions.Expect(first.Locator(".hello-recovery-codes li").First).ToBeVisibleAsync();
    }
}
