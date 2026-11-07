using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Hello;
using Testcontainers.PostgreSql;
using Xunit;

namespace Skopka.Hello.IntegrationTests;

public sealed partial class AuthenticationFlowTests
{
    private sealed class SiteAccountPolicy : IHelloSignInPolicy<IntegrationProfile>
    {
        public Task<bool> IsAllowedAsync(HelloSignInContext<IntegrationProfile> context, CancellationToken cancellationToken)
            => Task.FromResult(context.Account.Profile.DisplayName == context.Site.Id);
    }

    [Fact]
    public async Task SitesRestrictAccountMethodAndOnlineSessionAndResetOrigin()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        await using var app = await TestApplication.CreateAsync(postgres.GetConnectionString(), uiPathPrefix: "/identity", nativeBrowser: true,
            configureHello: options =>
            {
                options.PhoneOtpEnabled = true;
                options.Sites.Add(new HelloSiteOptions { Id = "main", PublicOrigin = new Uri("https://main.example.test") });
                options.Sites.Add(new HelloSiteOptions { Id = "staff", PublicOrigin = new Uri("https://staff.example.test"), PhoneOtpEnabled = false });
            },
            configureServices: services => services.AddSingleton<IHelloSignInPolicy<IntegrationProfile>, SiteAccountPolicy>());
        using var client = app.CreateClient(allowAutoRedirect: false);
        client.DefaultRequestHeaders.Host = "main.example.test";
        const string password = "correct horse battery staple";
        using var registration = await client.PostAsJsonAsync("/auth/register", new
        {
            userName = "site-staff",
            email = "site-staff@example.test",
            phone = (string?)null,
            profile = new { displayName = "staff", locale = "en" },
            password
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var rejected = await client.PostAsJsonAsync("/auth/login", new { login = "site-staff@example.test", password });
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        client.DefaultRequestHeaders.Host = "staff.example.test";
        var signedIn = await LoginAsync(client, "site-staff@example.test", password);
        using var staffAccount = await GetMeAsync(client, signedIn.AccessToken);
        var cookies = await LoginThroughUiAsync(client, "site-staff@example.test", password, null, "/identity/account");
        using var accountPage = await SendAsync(client, HttpMethod.Get, "/identity/account", cookies);
        var accountHtml = await accountPage.Content.ReadAsStringAsync();
        Assert.DoesNotContain("id=\"phone-binding\"", accountHtml);
        var csrf = ReadInputValue(accountHtml, "__RequestVerificationToken");
        using var forgedPhone = await SendFormAsync(client, "/identity/account?handler=BeginPhoneBinding", cookies,
            new Dictionary<string, string> { ["BindingPhone"] = "+79991234589", ["__RequestVerificationToken"] = csrf });
        Assert.Equal(HttpStatusCode.NotFound, forgedPhone.StatusCode);
        using var security = await SendAsync(client, HttpMethod.Get, "/identity/account/security", cookies);
        var securityHtml = await security.Content.ReadAsStringAsync();
        Assert.DoesNotContain("handler=BeginTotpPhone", securityHtml);
        using var phone = await client.PostAsJsonAsync("/auth/phone/request", new { phone = "+79991234589" });
        Assert.False(phone.IsSuccessStatusCode);
        using var reset = await client.PostAsJsonAsync("/auth/password-reset/request", new { email = "site-staff@example.test" });
        Assert.Equal(HttpStatusCode.Accepted, reset.StatusCode);
        var message = await app.WaitForMessageAsync(HelloAccountMessageKind.PasswordReset);
        Assert.Equal("https://staff.example.test", Assert.IsType<Uri>(message.ActionUrl).GetLeftPart(UriPartial.Authority));
        client.DefaultRequestHeaders.Host = "main.example.test";
        using var me = new HttpRequestMessage(HttpMethod.Get, "/account/me");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.AccessToken);
        using var denied = await client.SendAsync(me);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var mainRegistration = await client.PostAsJsonAsync("/auth/register", new
        {
            userName = "site-main",
            email = "site-main@example.test",
            phone = (string?)null,
            profile = new { displayName = "main", locale = "en" },
            password
        });
        Assert.Equal(HttpStatusCode.Created, mainRegistration.StatusCode);
        var mainLogin = await LoginAsync(client, "site-main@example.test", password);
        client.DefaultRequestHeaders.Host = "staff.example.test";
        using var wrongSiteRefresh = CreateCookieMutation(HttpMethod.Post, "/auth/refresh", mainLogin.Cookies);
        using var wrongSiteResponse = await client.SendAsync(wrongSiteRefresh);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongSiteResponse.StatusCode);
        client.DefaultRequestHeaders.Host = "main.example.test";
        var refreshed = await RefreshAsync(client, mainLogin.Cookies);
        Assert.Equal(mainLogin.SessionId, refreshed.SessionId);
    }
}
