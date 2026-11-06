using System.Net;
using System.Net.Http.Json;
using Skopka.Hello;
using Testcontainers.PostgreSql;
using Xunit;

namespace Skopka.Hello.IntegrationTests;

public sealed partial class AuthenticationFlowTests
{
    [Fact]
    public async Task UiPhoneBindingReportsOccupiedNumberOnlyAfterCorrectCode()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        await using var app = await TestApplication.CreateAsync(postgres.GetConnectionString(), uiPathPrefix: "/identity",
            configureHello: options => options.PhoneOtpEnabled = true,
            configureUi: options => { options.Localization.Enabled = true; options.Localization.DefaultCulture = "ru"; });
        using var client = app.CreateClient(allowAutoRedirect: false);
        const string password = "correct horse battery staple";
        foreach (var (name, phone) in new[] { ("ui-binding", (string?)null), ("ui-owner", "+79991234581") })
        {
            using var registered = await client.PostAsJsonAsync("/auth/register", new
            {
                userName = name,
                email = name + "@example.test",
                phone,
                profile = new { displayName = name, locale = "ru" },
                password
            });
            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        }
        var cookies = await LoginThroughUiAsync(client, "ui-binding@example.test", password, null, "/identity/account");
        using var page = await SendAsync(client, HttpMethod.Get, "/identity/account", cookies);
        var html = await page.Content.ReadAsStringAsync();
        var csrf = ReadInputValue(html, "__RequestVerificationToken");
        using var started = await SendFormAsync(client, "/identity/account?handler=BeginPhoneBinding", cookies,
            new Dictionary<string, string> { ["BindingPhone"] = "+79991234581", ["__RequestVerificationToken"] = csrf });
        var startedHtml = await started.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Этот номер телефона уже используется", WebUtility.HtmlDecode(startedHtml));
        var ticket = ReadInputValue(startedHtml, "BindingTicket");
        csrf = ReadInputValue(startedHtml, "__RequestVerificationToken");
        var message = await app.WaitForMessageAsync(HelloAccountMessageKind.PhoneSignInVerification);
        Assert.NotNull(message.VerificationCode);
        using var wrong = await SendFormAsync(client, "/identity/account?handler=CompletePhoneBinding", cookies,
            new Dictionary<string, string>
            {
                ["BindingTicket"] = ticket,
                ["BindingCode"] = message.VerificationCode == "000000" ? "111111" : "000000",
                ["__RequestVerificationToken"] = csrf
            });
        var wrongHtml = await wrong.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Этот номер телефона уже используется", WebUtility.HtmlDecode(wrongHtml));
        csrf = ReadInputValue(wrongHtml, "__RequestVerificationToken");
        using var conflict = await SendFormAsync(client, "/identity/account?handler=CompletePhoneBinding", cookies,
            new Dictionary<string, string>
            {
                ["BindingTicket"] = ticket,
                ["BindingCode"] = message.VerificationCode,
                ["__RequestVerificationToken"] = csrf
            });
        Assert.Contains("Этот номер телефона уже используется", WebUtility.HtmlDecode(await conflict.Content.ReadAsStringAsync()));
    }
}
