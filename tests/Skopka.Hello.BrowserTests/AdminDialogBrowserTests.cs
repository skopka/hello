using Microsoft.Playwright;
using Skopka.Hello.IntegrationTests;

namespace Skopka.Hello.BrowserTests;

public sealed class AdminDialogBrowserTests(BrowserFixture browser) : IClassFixture<BrowserFixture>
{
    [Fact]
    public async Task RealAdminPagesKeepTargetsFocusAndStepUpInDialogs()
    {
        await using var host = await AuthenticationFlowTests.AdminBrowserHost.CreateAsync();
        await using var context = await browser.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 1280, Height = 800 } });
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(host.Origin, "/hello/login").ToString());
        await page.Locator("[name='Input.Login']").FillAsync("browser-admin@example.test");
        await page.Locator("[name='Input.Password']").FillAsync("correct horse battery staple");
        await page.Locator("form:has([name='Input.Password']) button[type='submit']").ClickAsync();
        await page.GotoAsync(new Uri(host.Origin, "/hello/admin/users").ToString());
        var row = page.Locator(".admin-list-row").Filter(new() { HasText = "browser-target" });
        await Assertions.Expect(row).ToHaveCountAsync(1);
        Assert.True(await row.EvaluateAsync<bool>("e => e.getBoundingClientRect().height <= 64"));
        var button = row.Locator("[data-bs-toggle='modal']");
        var dialog = page.Locator($"#admin-user-{host.TargetId:N}");
        Assert.True(await page.Locator(".admin-list-row").CountAsync() >= 3);
        await page.SetViewportSizeAsync(1920, 900);
        Assert.True(await page.Locator(".admin-directory").EvaluateAsync<bool>("e => { const r = e.getBoundingClientRect(); return r.width <= 1280 && r.left < 40; }"));
        await ScreenshotAsync(page, "users-desktop.png");
        await button.ClickAsync();
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(dialog).ToContainTextAsync("browser-target");
        Assert.True(await dialog.EvaluateAsync<bool>("e => !!document.getElementById(e.getAttribute('aria-labelledby'))"));
        await ScreenshotAsync(page, "user-modal-desktop.png");
        await page.AddStyleTagAsync(new() { Content = ":root { --skopka-admin-background: #15191e; --skopka-admin-surface: #20252b; --skopka-admin-text: #f5f7fa; --skopka-admin-muted: #bdc9d6; --skopka-admin-border: #8c99a8; --skopka-admin-primary: #aab4ff; --skopka-admin-primary-dark: #8f9bff; }" });
        Assert.Equal("rgb(32, 37, 43)", await dialog.Locator(".modal-content").EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor"));
        Assert.Equal("rgb(245, 247, 250)", await dialog.Locator(".modal-content").EvaluateAsync<string>("e => getComputedStyle(e).color"));
        await ScreenshotAsync(page, "user-modal-dark.png");
        await Assertions.Expect(dialog).ToBeFocusedAsync();
        for (var i = 0; i < 25; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            Assert.True(await dialog.EvaluateAsync<bool>("e => e.contains(document.activeElement)"));
        }
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(dialog).ToBeHiddenAsync();
        await Assertions.Expect(button).ToBeFocusedAsync();
        await button.ClickAsync();
        await Assertions.Expect(dialog).ToBeFocusedAsync();
        await dialog.Locator("[data-bs-dismiss='modal']").ClickAsync();
        await Assertions.Expect(dialog).ToBeHiddenAsync();
        await Assertions.Expect(button).ToBeFocusedAsync();
        await page.SetViewportSizeAsync(375, 812);
        await button.ClickAsync();
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
        await ScreenshotAsync(page, "user-modal-narrow.png");
        var block = dialog.Locator("form:has(input[name='action'][value='block'])");
        await block.Locator("input[name='reason']").FillAsync("Browser review");
        await block.Locator("button[type='submit']").ClickAsync();
        var pending = page.Locator("#admin-pending-dialog");
        await Assertions.Expect(pending).ToBeVisibleAsync();
        await Assertions.Expect(pending).ToContainTextAsync("browser-target");
        await Assertions.Expect(pending).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(pending).ToBeHiddenAsync();
        var reopen = page.Locator("button[data-bs-target='#admin-pending-dialog']");
        await reopen.ClickAsync();
        await Assertions.Expect(pending).ToBeVisibleAsync();
        await Assertions.Expect(pending).ToBeFocusedAsync();
        await Assertions.Expect(pending.Locator("[name='userId']")).ToHaveValueAsync(host.TargetId.ToString());
        await Assertions.Expect(pending.Locator("[name='reason']")).ToHaveValueAsync("Browser review");
        Assert.NotNull(host.VerificationCode);
        var wrongCode = host.VerificationCode == "000000" ? "111111" : "000000";
        await pending.Locator("[name='verificationCode']").FillAsync(wrongCode);
        await pending.Locator("button[type='submit']").ClickAsync();
        await Assertions.Expect(pending).ToBeVisibleAsync();
        await Assertions.Expect(pending.Locator("[role='alert']")).ToBeVisibleAsync();
        Assert.NotNull(host.VerificationCode);
        await pending.Locator("[name='verificationCode']").FillAsync(host.VerificationCode!);
        await pending.Locator("button[type='submit']").ClickAsync();
        await Assertions.Expect(page.Locator("[role='status']")).ToBeVisibleAsync();
        await page.GotoAsync(new Uri(host.Origin, "/hello/admin/roles").ToString());
        var roleRow = page.Locator(".admin-list-row").Filter(new() { HasText = "browser-reviewer" });
        await Assertions.Expect(roleRow).ToHaveCountAsync(1);
        Assert.True(await roleRow.EvaluateAsync<bool>("e => e.getBoundingClientRect().height <= 64"));
        Assert.True(await page.Locator(".admin-list-row").CountAsync() >= 3);
        await ScreenshotAsync(page, "roles-narrow.png");
        var roleButton = roleRow.Locator("[data-bs-toggle='modal']");
        await roleButton.ClickAsync();
        var roleDialog = page.Locator($"#admin-role-{host.RoleId:N}");
        await Assertions.Expect(roleDialog).ToBeVisibleAsync();
        await Assertions.Expect(roleDialog.Locator("input[name='name']")).ToHaveValueAsync("browser-reviewer");
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
        await Assertions.Expect(roleDialog).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(roleDialog).ToBeHiddenAsync();
        await Assertions.Expect(roleButton).ToBeFocusedAsync();
        await page.SetViewportSizeAsync(1280, 800);
        await ScreenshotAsync(page, "roles-desktop.png");
        await roleButton.ClickAsync();
        await Assertions.Expect(roleDialog).ToBeFocusedAsync();
        await roleDialog.Locator("input[name='name']").FillAsync("   ");
        await roleDialog.Locator("textarea[name='description']").FillAsync("Preserve this description");
        await roleDialog.Locator("form:has(input[name='action'][value='update']) button[type='submit']").ClickAsync();
        await Assertions.Expect(roleDialog).ToBeVisibleAsync();
        await Assertions.Expect(roleDialog.Locator("[role='alert']")).ToBeVisibleAsync();
        await Assertions.Expect(roleDialog.Locator("textarea[name='description']")).ToHaveValueAsync("Preserve this description");
    }

    [Theory]
    [InlineData("en", "OIDC clients", "Create client", "Client pages")]
    [InlineData("ru", "Клиенты OIDC", "Создать клиента", "Страницы клиентов")]
    public async Task ClientValidationSummaryAppearsOnlyForErrors(string culture, string title, string createTitle, string pages)
    {
        await using var host = await AuthenticationFlowTests.AdminBrowserHost.CreateAsync();
        await using var context = await browser.Browser.NewContextAsync(new() { Locale = culture });
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(host.Origin, "/hello/login").ToString());
        await page.Locator("[name='Input.Login']").FillAsync("browser-admin@example.test");
        await page.Locator("[name='Input.Password']").FillAsync("correct horse battery staple");
        await page.Locator("form:has([name='Input.Password']) button[type='submit']").ClickAsync();
        await page.GotoAsync(new Uri(host.Origin, "/hello/admin/clients").ToString());
        await Assertions.Expect(page).ToHaveTitleAsync(new System.Text.RegularExpressions.Regex(System.Text.RegularExpressions.Regex.Escape(title)));
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync(title);
        await Assertions.Expect(page.Locator(".admin-nav-link.active")).ToHaveTextAsync(title);
        Assert.DoesNotContain("Admin.Clients.", await page.Locator("body").InnerTextAsync(), StringComparison.Ordinal);
        await Assertions.Expect(page.GetByRole(AriaRole.Navigation, new() { Name = pages })).ToHaveCountAsync(1);
        var create = page.Locator("#admin-client-create");
        await Assertions.Expect(create).ToBeHiddenAsync();
        await page.Locator("[data-bs-target='#admin-client-create']").ClickAsync();
        await Assertions.Expect(create).ToBeVisibleAsync();
        await Assertions.Expect(create.Locator("h2")).ToHaveTextAsync(createTitle);
        await create.Locator("[name='ClientId']").FillAsync("browser-client");
        await create.Locator("[name='DisplayName']").FillAsync("Browser client");
        await create.Locator("[name='Type']").SelectOptionAsync("1");
        await create.Locator("[name='SelectedScopes'][value='openid']").CheckAsync();
        await create.Locator("[name='SelectedScopes'][value='profile']").CheckAsync();
        await create.Locator("[name='RedirectUris']").FillAsync("not a valid URI");
        await create.Locator("button[type='submit']").ClickAsync();
        await Assertions.Expect(create).ToBeVisibleAsync();
        await Assertions.Expect(create.Locator("[role='alert']")).ToBeVisibleAsync();
        await Assertions.Expect(create.Locator("[name='SelectedScopes'][value='openid']")).ToBeCheckedAsync();
        await ClientScreenshotAsync(page, $"{culture}-invalid.png");
        await create.Locator("[name='RedirectUris']").FillAsync("https://client.example.test/callback");
        await create.Locator("button[type='submit']").ClickAsync();
        var pending = page.Locator("#admin-client-pending");
        await Assertions.Expect(pending).ToBeVisibleAsync();
        Assert.NotNull(host.VerificationCode);
        await pending.Locator("[name='VerificationCode']").FillAsync(host.VerificationCode!);
        await pending.Locator("button[type='submit']").ClickAsync();
        await Assertions.Expect(page.Locator(".alert-warning[role='status'] pre")).ToBeVisibleAsync();
        var row = page.Locator(".admin-list-row").Filter(new() { HasText = "browser-client" });
        await Assertions.Expect(row).ToHaveCountAsync(1);
        Assert.True(await row.EvaluateAsync<bool>("e => e.getBoundingClientRect().height <= 64"));
        await row.Locator("button").ClickAsync();
        var dialogTarget = await row.Locator("button").GetAttributeAsync("data-bs-target");
        var dialog = page.Locator(dialogTarget!);
        await Assertions.Expect(dialog).ToBeFocusedAsync();
        await Assertions.Expect(dialog.Locator("[name='SelectedScopes'][value='openid']")).ToBeCheckedAsync();
        await Assertions.Expect(dialog.Locator("[name='DisplayName']")).ToHaveValueAsync("Browser client");
        await Assertions.Expect(dialog.Locator("pre")).ToHaveCountAsync(0);
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(dialog).ToBeHiddenAsync();
        await Assertions.Expect(row.Locator("button")).ToBeFocusedAsync();
        await page.SetViewportSizeAsync(375, 812);
        await row.Locator("button").ClickAsync();
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));

    }

    private static async Task ClientScreenshotAsync(IPage page, string name)
    {
        var directory = Path.Combine(FindRoot(), "artifacts", "client-localization");
        Directory.CreateDirectory(directory);
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, name), FullPage = true, Animations = ScreenshotAnimations.Disabled });
    }

    private static async Task ScreenshotAsync(IPage page, string name)
    {
        var directory = Path.Combine(FindRoot(), "artifacts", "admin-compact");
        Directory.CreateDirectory(directory);
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, name), FullPage = true, Animations = ScreenshotAnimations.Disabled });
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Skopka.Hello.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
