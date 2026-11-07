using Microsoft.Playwright;
using Skopka.Hello.IntegrationTests;

namespace Skopka.Hello.BrowserTests;

public sealed partial class TotpEnrollmentBrowserTests
{
    [Fact]
    public async Task AccessLinkFragmentSetsPasswordAndAuthenticatorOnce()
    {
        await using var host = await AuthenticationFlowTests.AdminBrowserHost.CreateAsync(accessLinksEnabled: true);
        var link = await host.IssueTargetAccessLinkAsync();
        await using var context = await browser.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var requests = new List<string>();
        page.Request += (_, request) => requests.Add(request.Url);
        await page.GotoAsync(link.ToString());
        await Assertions.Expect(page.Locator("form[action*='handler=Begin']")).ToBeVisibleAsync();
        Assert.DoesNotContain("token=", page.Url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(requests, request => request.Contains("token=", StringComparison.OrdinalIgnoreCase));
        await page.Locator("form[action*='handler=Begin'] button").ClickAsync();
        var secret = await page.Locator("section.hello-card > p code").InnerTextAsync();
        await page.Locator("input[name='Password']").FillAsync("another strong password 123");
        await page.Locator("input[name='ConfirmPassword']").FillAsync("another strong password 123");
        await page.Locator("input[name='Code']").FillAsync(ComputeTotp(secret));
        await page.Locator("form[action*='handler=Complete'] button").ClickAsync();
        await Assertions.Expect(page.Locator("section.hello-card li code").First).ToBeVisibleAsync();
        Assert.DoesNotContain("another strong password 123", await page.ContentAsync(), StringComparison.Ordinal);
        await page.GotoAsync(link.ToString());
        await Assertions.Expect(page.Locator(".hello-validation-summary")).ToContainTextAsync("invalid, expired or already used");
    }
}
