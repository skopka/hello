using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
            configureUi: options => { options.Localization.Enabled = true; options.Localization.DefaultCulture = "ru"; options.Account.PhoneBindingEnabled = true; options.Account.Phone = Skopka.Hello.UI.HelloUiAccountFieldMode.ReadOnly; });
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
        Assert.Contains("id=\"phone-binding\"", html);
        Assert.DoesNotContain("handler=ChangePhone", html);
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
    [Fact]
    public async Task ReplacingConfirmedPhoneRequiresProofAndConsumesTicket()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        await using var app = await TestApplication.CreateAsync(postgres.GetConnectionString(),
            configureHello: options => options.PhoneOtpEnabled = true);
        using var client = app.CreateClient(allowAutoRedirect: false);
        const string password = "correct horse battery staple";
        const string oldPhone = "+79991234582";
        const string newPhone = "+79991234583";
        using var registered = await client.PostAsJsonAsync("/auth/register", new
        {
            userName = "binding-replace",
            email = "binding-replace@example.test",
            phone = oldPhone,
            profile = new { displayName = "Replacement", locale = "en" },
            password
        });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        await ConfirmPhoneForOtpAsync(client, app, oldPhone);
        var login = await LoginAsync(client, "binding-replace@example.test", password);
        using var begun = await SendAuthorizedJsonAsync(client, HttpMethod.Post, "/account/phone/binding",
            login.AccessToken, new { phone = newPhone });
        Assert.Equal(HttpStatusCode.OK, begun.StatusCode);
        using var payload = JsonDocument.Parse(await begun.Content.ReadAsStringAsync());
        var ticket = payload.RootElement.GetProperty("ticket").GetString();
        var message = await app.WaitForMessageAsync(HelloAccountMessageKind.PhoneSignInVerification);
        Assert.NotNull(message.VerificationCode);
        using var before = await GetMeAsync(client, login.AccessToken);
        Assert.Equal(oldPhone, before.RootElement.GetProperty("phone").GetString());
        Assert.True(before.RootElement.GetProperty("phoneConfirmed").GetBoolean());
        using var wrong = await SendAuthorizedJsonAsync(client, HttpMethod.Post, "/account/phone/binding/complete",
            login.AccessToken, new { ticket, code = message.VerificationCode == "000000" ? "111111" : "000000" });
        Assert.False(wrong.IsSuccessStatusCode);
        using var afterWrong = await GetMeAsync(client, login.AccessToken);
        Assert.Equal(oldPhone, afterWrong.RootElement.GetProperty("phone").GetString());
        Assert.True(afterWrong.RootElement.GetProperty("phoneConfirmed").GetBoolean());
        using var done = await SendAuthorizedJsonAsync(client, HttpMethod.Post, "/account/phone/binding/complete",
            login.AccessToken, new { ticket, code = message.VerificationCode });
        Assert.Equal(HttpStatusCode.NoContent, done.StatusCode);
        using var after = await GetMeAsync(client, login.AccessToken);
        Assert.Equal(newPhone, after.RootElement.GetProperty("phone").GetString());
        Assert.True(after.RootElement.GetProperty("phoneConfirmed").GetBoolean());
        using var replay = await SendAuthorizedJsonAsync(client, HttpMethod.Post, "/account/phone/binding/complete",
            login.AccessToken, new { ticket, code = message.VerificationCode });
        Assert.False(replay.IsSuccessStatusCode);
    }

    [Fact]
    public async Task DisabledUiBindingAndRemovedPhoneHandlerRejectForgedPosts()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        await using var app = await TestApplication.CreateAsync(postgres.GetConnectionString(), uiPathPrefix: "/identity",
            configureHello: options => options.PhoneOtpEnabled = true,
            configureUi: options => options.Account.PhoneBindingEnabled = false);
        using var client = app.CreateClient(allowAutoRedirect: false);
        const string password = "correct horse battery staple";
        using var registered = await client.PostAsJsonAsync("/auth/register", new
        {
            userName = "binding-disabled",
            email = "binding-disabled@example.test",
            phone = (string?)null,
            profile = new { displayName = "Disabled", locale = "en" },
            password
        });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var login = await LoginAsync(client, "binding-disabled@example.test", password);
        using var initial = await GetMeAsync(client, login.AccessToken);
        var version = initial.RootElement.GetProperty("version").GetInt64();
        var cookies = await LoginThroughUiAsync(client, "binding-disabled@example.test", password, null, "/identity/account");
        using var page = await SendAsync(client, HttpMethod.Get, "/identity/account", cookies);
        var html = await page.Content.ReadAsStringAsync();
        Assert.DoesNotContain("id=\"phone-binding\"", html);
        var csrf = ReadInputValue(html, "__RequestVerificationToken");
        using var legacy = await SendFormAsync(client, "/identity/account?handler=ChangePhone", cookies,
            new Dictionary<string, string> { ["PhoneInput.ExpectedVersion"] = version.ToString(System.Globalization.CultureInfo.InvariantCulture), ["PhoneInput.Phone"] = "+79991234584", ["__RequestVerificationToken"] = csrf });
        using var begun = await SendFormAsync(client, "/identity/account?handler=BeginPhoneBinding", cookies,
            new Dictionary<string, string> { ["BindingPhone"] = "+79991234584", ["__RequestVerificationToken"] = csrf });
        Assert.False(begun.IsSuccessStatusCode);
        Assert.DoesNotContain(app.Messages, entry => entry.Kind == HelloAccountMessageKind.PhoneSignInVerification);
        using var complete = await SendFormAsync(client, "/identity/account?handler=CompletePhoneBinding", cookies,
            new Dictionary<string, string> { ["BindingTicket"] = "forged", ["BindingCode"] = "123456", ["__RequestVerificationToken"] = csrf });
        Assert.False(complete.IsSuccessStatusCode);
        using var after = await GetMeAsync(client, login.AccessToken);
        Assert.Equal(version, after.RootElement.GetProperty("version").GetInt64());
        Assert.Equal(JsonValueKind.Null, after.RootElement.GetProperty("phone").ValueKind);
    }

}
