using Microsoft.Playwright;
using Skopka.Hello.IntegrationTests;

namespace Skopka.Hello.BrowserTests;

/// <summary>
/// Every Hello form submission ends with a GET: refresh never repeats it, a
/// duplicate confirmation ends like the first one and the person returns to
/// the dialog they started from.
/// </summary>
[Collection("Container-backed browser tests")]
public sealed class AdminPrgBrowserTests(BrowserFixture browser) : IClassFixture<BrowserFixture>
{
    private const string Password = "correct horse battery staple";

    // An empty validation summary keeps one hidden placeholder item.
    private const string AdminErrors = ".admin-validation li:not([style*='display:none'])";

    [Fact]
    public async Task LoginErrorSurvivesRefreshWithoutResubmittingOrKeepingThePassword()
    {
        await using var host = await AuthenticationFlowTests.AdminBrowserHost.CreateAsync();
        await using var context = await browser.Browser.NewContextAsync(new() { Locale = "en" });
        var page = await context.NewPageAsync();
        var posts = CountPosts(page);
        await page.GotoAsync(new Uri(host.Origin, "/hello/login?ReturnUrl=%2Fhello%2Fadmin%2Fusers").ToString());
        await page.Locator("[name='Input.Login']").FillAsync("browser-admin@example.test");
        await page.Locator("[name='Input.Password']").FillAsync("wrong password value");
        await page.Locator("form:has([name='Input.Password']) button[type='submit']").ClickAsync();

        await Assertions.Expect(page.Locator(".hello-validation li")).Not.ToHaveCountAsync(0);
        Assert.Single(posts);
        Assert.Contains("hello-prg=", page.Url, StringComparison.Ordinal);
        Assert.Contains("ReturnUrl=", page.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("handler=", page.Url, StringComparison.OrdinalIgnoreCase);
        var error = await page.Locator(".hello-validation").InnerTextAsync();

        await page.ReloadAsync();

        Assert.Single(posts);
        Assert.Equal(error, await page.Locator(".hello-validation").InnerTextAsync());
        await Assertions.Expect(page.Locator("[name='Input.Login']")).ToHaveValueAsync("browser-admin@example.test");
        await Assertions.Expect(page.Locator("[name='Input.Password']")).ToHaveValueAsync(string.Empty);
        Assert.DoesNotContain("wrong password value", await page.ContentAsync(), StringComparison.Ordinal);

        // The retained outcome belongs to this browser only.
        await using var other = await browser.Browser.NewContextAsync(new() { Locale = "en" });
        var stranger = await other.NewPageAsync();
        await stranger.GotoAsync(page.Url);
        await Assertions.Expect(stranger.Locator(".hello-validation li")).ToHaveCountAsync(0);

        await page.Locator("[name='Input.Password']").FillAsync(Password);
        await page.Locator("form:has([name='Input.Password']) button[type='submit']").ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/hello/admin/users", StringComparison.Ordinal));
        // Back after the successful submission shows the page afresh, not the old error.
        await page.GoBackAsync();
        await Assertions.Expect(page.Locator(".hello-validation li")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData("/hello/register", "Input.DisplayName", "Browser Visitor", "Input.Password")]
    [InlineData("/hello/forgot-password", "Input.Email", "not-an-email", null)]
    [InlineData("/hello/resend-confirmation", "Input.Email", "not-an-email", null)]
    public async Task FormErrorsSurviveRefreshWithoutResubmitting(
        string path,
        string keptField,
        string keptValue,
        string? secretField)
    {
        await using var host = await AuthenticationFlowTests.AdminBrowserHost.CreateAsync();
        await using var context = await browser.Browser.NewContextAsync(new() { Locale = "ru" });
        var page = await context.NewPageAsync();
        var posts = CountPosts(page);
        await page.GotoAsync(new Uri(host.Origin, path).ToString());
        await page.Locator($"[name='{keptField}']").FillAsync(keptValue);
        if (secretField is not null)
        {
            await page.Locator($"[name='{secretField}']").FillAsync("typed secret value");
        }

        // submit() skips the browser's own field validation, so the server
        // answers with its errors.
        await page.Locator($"form:has([name='{keptField}'])").EvaluateAsync("f => HTMLFormElement.prototype.submit.call(f)");
        await page.WaitForURLAsync(url => url.Contains("hello-prg=", StringComparison.Ordinal));
        var errors = page.Locator(".field-validation-error, .hello-validation li");
        await Assertions.Expect(errors).Not.ToHaveCountAsync(0);
        var shown = await errors.AllInnerTextsAsync();
        Assert.Single(posts);
        Assert.DoesNotContain("handler=", page.Url, StringComparison.OrdinalIgnoreCase);

        await page.ReloadAsync();

        Assert.Single(posts);
        Assert.Equal(shown, await errors.AllInnerTextsAsync());
        await Assertions.Expect(page.Locator($"[name='{keptField}']")).ToHaveValueAsync(keptValue);
        if (secretField is not null)
        {
            await Assertions.Expect(page.Locator($"[name='{secretField}']")).ToHaveValueAsync(string.Empty);
        }

        Assert.DoesNotContain("typed secret value", await page.ContentAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RoleConfirmationSurvivesRefreshDuplicatesAndReturnsToTheUserDialog()
    {
        await using var host = await AuthenticationFlowTests.AdminBrowserHost.CreateAsync();
        await using var context = await browser.Browser.NewContextAsync(new() { Locale = "ru", ViewportSize = new() { Width = 1280, Height = 900 } });
        var page = await context.NewPageAsync();
        await SignInAsync(page, host);
        var posts = CountPosts(page);
        await page.GotoAsync(new Uri(host.Origin, "/hello/admin/users?Search=browser-target&Status=Active").ToString());
        var dialog = page.Locator($"#admin-user-{host.TargetId:N}");
        await page.Locator($"[data-bs-target='#admin-user-{host.TargetId:N}']").ClickAsync();
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await dialog.Locator("form:has(input[name='action'][value='assign']) select[name='roleId']")
            .SelectOptionAsync(host.RoleId.ToString());
        await dialog.Locator("form:has(input[name='action'][value='assign']) button[type='submit']").ClickAsync();

        var pending = page.Locator("#admin-pending-role-dialog");
        await Assertions.Expect(pending).ToBeVisibleAsync();
        Assert.DoesNotContain("handler=", page.Url, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Search=browser-target", page.Url, StringComparison.Ordinal);
        var issued = host.AdminVerificationCount;
        await page.ReloadAsync();
        await Assertions.Expect(pending).ToBeVisibleAsync();
        Assert.Equal(issued, host.AdminVerificationCount);

        await pending.Locator("[name='verificationCode']").FillAsync(host.VerificationCode == "000000" ? "111111" : "000000");
        await pending.Locator("button[type='submit']").ClickAsync();
        await Assertions.Expect(pending).ToBeVisibleAsync();
        await Assertions.Expect(pending.Locator(AdminErrors)).Not.ToHaveCountAsync(0);
        var postsBeforeReload = posts.Count;
        await page.ReloadAsync();
        Assert.Equal(postsBeforeReload, posts.Count);
        await Assertions.Expect(pending).ToBeVisibleAsync();
        await Assertions.Expect(pending.Locator(AdminErrors)).Not.ToHaveCountAsync(0);
        await Assertions.Expect(pending.Locator("[name='verificationCode']")).ToHaveValueAsync(string.Empty);
        Assert.False(await host.TargetHasRoleAsync());

        // Two submissions of one confirmation: the browser abandons the first
        // response, as with a double click or Enter and a click.
        await pending.Locator("[name='verificationCode']").FillAsync(host.VerificationCode!);
        var completions = 0;
        var secondSent = new TaskCompletionSource();
        await page.RouteAsync("**/hello/admin/users?handler=CompleteRoleAction*", async route =>
        {
            if (Interlocked.Increment(ref completions) == 1)
            {
                var response = await route.FetchAsync(new() { MaxRedirects = 0 });
                await secondSent.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await route.FulfillAsync(new() { Response = response });
                return;
            }

            secondSent.TrySetResult();
            await route.ContinueAsync();
        });
        // The second submission is sent while the first one is still on its
        // way; submit() bypasses the page script's own duplicate guard.
        await pending.Locator("form").EvaluateAsync(
            "f => { HTMLFormElement.prototype.submit.call(f); setTimeout(() => HTMLFormElement.prototype.submit.call(f), 300); }");

        await Assertions.Expect(dialog).ToBeVisibleAsync();
        Assert.Equal(2, completions);
        Assert.True(await host.TargetHasRoleAsync());
        await Assertions.Expect(dialog.Locator(".alert-success[role='status']")).ToBeVisibleAsync();
        await Assertions.Expect(dialog.Locator(".admin-role-membership-list")).ToContainTextAsync("browser-reviewer");
        await Assertions.Expect(page.Locator(AdminErrors)).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("[data-bs-target='#admin-pending-role-dialog']")).ToHaveCountAsync(0);
        Assert.Contains("Search=browser-target", page.Url, StringComparison.Ordinal);
        Assert.Contains("Status=Active", page.Url, StringComparison.Ordinal);
        Assert.Contains($"Manage={host.TargetId}", page.Url, StringComparison.OrdinalIgnoreCase);
        await page.UnrouteAllAsync();

        // The success message is shown once; the dialog stays the context.
        await page.ReloadAsync();
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".alert-success[role='status']")).ToHaveCountAsync(0);

        // A confirmation spent elsewhere (another instance, an earlier tab):
        // the page says what is in effect instead of a bare "invalid or expired".
        await dialog.Locator($"form:has(input[name='action'][value='remove']):has(input[name='roleId'][value='{host.RoleId}']) button[type='submit']").ClickAsync();
        await Assertions.Expect(pending).ToBeVisibleAsync();
        var challengeId = Guid.Parse((await pending.Locator("[name='challengeId']").InputValueAsync())!);
        var code = host.VerificationCode!;
        await host.CompleteTargetRoleActionThroughApiAsync("remove", challengeId, code);
        Assert.False(await host.TargetHasRoleAsync());
        await pending.Locator("[name='verificationCode']").FillAsync(code);
        await pending.Locator("button[type='submit']").ClickAsync();
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(dialog.Locator("[data-admin-outcome]")).ToContainTextAsync("browser-reviewer");
        await Assertions.Expect(page.Locator(AdminErrors)).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("[data-bs-target='#admin-pending-role-dialog']")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task BadgesShowAccountAttributesAndFilterTheList()
    {
        await using var host = await AuthenticationFlowTests.AdminBrowserHost.CreateAsync();
        await using var context = await browser.Browser.NewContextAsync(new() { Locale = "en" });
        var page = await context.NewPageAsync();
        await SignInAsync(page, host);
        await page.GotoAsync(new Uri(host.Origin, "/hello/admin/users").ToString());

        var admin = page.Locator(".admin-list-row").Filter(new() { HasText = "browser-admin" });
        var target = page.Locator(".admin-list-row").Filter(new() { HasText = "browser-target" });
        await Assertions.Expect(admin.Locator("[data-admin-badge='admin']")).ToHaveTextAsync("Administrator");
        await Assertions.Expect(admin.Locator("[data-admin-badge='staff']")).ToHaveCountAsync(0);
        await Assertions.Expect(target.Locator("[data-admin-badge='staff']")).ToHaveTextAsync("Staff");
        await Assertions.Expect(target.Locator("[data-admin-badge='admin']")).ToHaveCountAsync(0);

        await page.Locator("#admin-badge").SelectOptionAsync("admin");
        await page.Locator(".admin-filter-form button[type='submit']").ClickAsync();
        await Assertions.Expect(page.Locator(".admin-list-row")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".admin-list-row")).ToContainTextAsync("browser-admin");
        Assert.Contains("Badge=admin", page.Url, StringComparison.Ordinal);

        await page.Locator("#admin-badge").SelectOptionAsync("staff");
        await page.Locator(".admin-filter-form button[type='submit']").ClickAsync();
        await Assertions.Expect(page.Locator(".admin-list-row")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".admin-list-row")).ToContainTextAsync("browser-target");
        await Assertions.Expect(page.Locator("#admin-badge")).ToHaveValueAsync("staff");
    }

    [Fact]
    public async Task RoleMembersCanBeListedAddedAndRemovedFromTheRolesPage()
    {
        await using var host = await AuthenticationFlowTests.AdminBrowserHost.CreateAsync();
        await using var context = await browser.Browser.NewContextAsync(new() { Locale = "ru", ViewportSize = new() { Width = 1280, Height = 900 } });
        var page = await context.NewPageAsync();
        await SignInAsync(page, host);
        await page.GotoAsync(new Uri(host.Origin, "/hello/admin/roles").ToString());
        await page.Locator($"[data-admin-role-members='{host.RoleId}']").ClickAsync();

        var dialog = page.Locator($"#admin-role-{host.RoleId:N}");
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(dialog.Locator("[data-admin-no-members]")).ToBeVisibleAsync();
        await dialog.Locator("[name='AddSearch']").FillAsync("browser-target");
        await dialog.Locator("form:has([name='AddSearch']) button[type='submit']").ClickAsync();
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        var candidate = dialog.Locator($"[data-admin-candidate='{host.TargetId}']");
        await Assertions.Expect(candidate).ToBeVisibleAsync();
        await candidate.Locator("button[type='submit']").ClickAsync();

        var pending = page.Locator("#admin-pending-dialog");
        await Assertions.Expect(pending).ToBeVisibleAsync();
        await Assertions.Expect(pending).ToContainTextAsync("browser-target");
        await page.ReloadAsync();
        await Assertions.Expect(pending).ToBeVisibleAsync();
        await pending.Locator("[name='verificationCode']").FillAsync(host.VerificationCode!);
        await pending.Locator("button[type='submit']").ClickAsync();

        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(dialog.Locator(".alert-success[role='status']")).ToBeVisibleAsync();
        var member = dialog.Locator($"[data-admin-member='{host.TargetId}']");
        await Assertions.Expect(member).ToContainTextAsync("browser-target");
        await Assertions.Expect(member.Locator("[data-admin-badge], .admin-user-badge")).ToContainTextAsync("Staff");
        Assert.True(await host.TargetHasRoleAsync());
        Assert.Contains($"Open={host.RoleId}", page.Url, StringComparison.OrdinalIgnoreCase);

        await dialog.Locator("input[name='MemberSearch']:not([type='hidden'])").FillAsync("nobody-matches-this");
        await dialog.Locator("form:has(input[name='MemberSearch']:not([type='hidden'])) button[type='submit']").ClickAsync();
        await Assertions.Expect(dialog.Locator("[data-admin-no-members]")).ToBeVisibleAsync();
        await dialog.Locator("input[name='MemberSearch']:not([type='hidden'])").FillAsync("browser-target");
        await dialog.Locator("form:has(input[name='MemberSearch']:not([type='hidden'])) button[type='submit']").ClickAsync();
        await member.Locator("button[type='submit']").ClickAsync();
        await Assertions.Expect(pending).ToBeVisibleAsync();
        await pending.Locator("[name='verificationCode']").FillAsync(host.VerificationCode!);
        await pending.Locator("button[type='submit']").ClickAsync();

        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(member).ToHaveCountAsync(0);
        Assert.False(await host.TargetHasRoleAsync());
        Assert.Contains("MemberSearch=browser-target", page.Url, StringComparison.Ordinal);
    }

    private static async Task SignInAsync(IPage page, AuthenticationFlowTests.AdminBrowserHost host)
    {
        await page.GotoAsync(new Uri(host.Origin, "/hello/login").ToString());
        await page.Locator("[name='Input.Login']").FillAsync("browser-admin@example.test");
        await page.Locator("[name='Input.Password']").FillAsync(Password);
        await page.Locator("form:has([name='Input.Password']) button[type='submit']").ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/hello/login", StringComparison.Ordinal));
    }

    private static List<string> CountPosts(IPage page)
    {
        List<string> posts = [];
        page.Request += (_, request) =>
        {
            if (request.Method == "POST" && request.IsNavigationRequest)
            {
                posts.Add(request.Url);
            }
        };
        return posts;
    }
}
