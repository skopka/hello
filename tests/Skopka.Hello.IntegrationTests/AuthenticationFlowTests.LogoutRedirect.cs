using System.Net;
using Testcontainers.PostgreSql;

namespace Skopka.Hello.IntegrationTests;

public sealed partial class AuthenticationFlowTests
{
    [Theory]
    [InlineData(null, "Logout")]
    [InlineData("/", "Logout")]
    [InlineData("/welcome", "Logout")]
    [InlineData(null, "LogoutAll")]
    [InlineData("/", "LogoutAll")]
    [InlineData(null, "Revoke")]
    [InlineData("/", "Revoke")]
    public async Task ExplicitLogoutUsesOnlyHostConfiguredLocalRedirect(string? redirectPath, string handler)
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        const string prefix = "/identity";
        await using var app = await TestApplication.CreateAsync(postgres.GetConnectionString(),
            uiPathPrefix: prefix,
            configureUi: options => options.PostLogoutRedirectPath = redirectPath);
        using var client = app.CreateClient(allowAutoRedirect: false);
        await RegisterUiAccountAsync(client, "logout-member", "logout@example.test", "Logout member");
        Dictionary<string, string> cookies = new(StringComparer.Ordinal);
        using var loginPage = await SendAsync(client, HttpMethod.Get, prefix + "/login", cookies);
        MergeCookies(cookies, loginPage);
        using var login = await SendFormAsync(client, prefix + "/login", cookies,
            new Dictionary<string, string>
            {
                ["Input.Login"] = "logout@example.test",
                ["Input.Password"] = "correct horse battery staple",
                ["__RequestVerificationToken"] = ReadInputValue(await loginPage.Content.ReadAsStringAsync(), "__RequestVerificationToken"),
            });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        MergeCookies(cookies, login);
        var pagePath = prefix + (handler == "Logout" ? "/account" : "/account/sessions");
        using var page = await SendAsync(client, HttpMethod.Get, pagePath, cookies);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        MergeCookies(cookies, page);
        var html = await page.Content.ReadAsStringAsync();
        var target = pagePath + "?handler=" + handler + "&returnUrl=https://outside.example/";
        using var missingAntiforgery = await SendFormAsync(client, target, cookies, new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.BadRequest, missingAntiforgery.StatusCode);
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ReadInputValue(html, "__RequestVerificationToken"),
            ["ReturnUrl"] = "//outside.example/",
            ["PostLogoutRedirectPath"] = "https://outside.example/",
        };
        if (handler == "Revoke") form["sessionId"] = ReadInputValue(html, "sessionId");
        using var logout = await SendFormAsync(client, target, cookies, form);
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(redirectPath ?? prefix + "/login", logout.Headers.Location?.OriginalString);
        MergeCookies(cookies, logout);
        using var accountAfterLogout = await SendAsync(client, HttpMethod.Get, prefix + "/account", cookies);
        Assert.Equal(HttpStatusCode.Redirect, accountAfterLogout.StatusCode);
        Assert.NotNull(accountAfterLogout.Headers.Location);
        Assert.Equal(prefix + "/login", new Uri(client.BaseAddress!, accountAfterLogout.Headers.Location).AbsolutePath);
    }
}
