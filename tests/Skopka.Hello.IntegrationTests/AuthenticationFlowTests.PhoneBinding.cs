using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Skopka.Hello;
using Skopka.Identity.Errors;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Abstraction.OperationResult;
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
        var actor = await LoginAsync(client, "ui-binding@example.test", password);
        using (var scope = app.Services.CreateScope())
        {
            var delegated = scope.ServiceProvider.GetRequiredService<IHelloDelegatedPhoneRegistration<IntegrationProfile>>();
            var denied = await delegated.BeginAsync(actor.AccessToken, "+79991234581", null, CancellationToken.None);
            Assert.False(denied.IsSuccess);
            Assert.DoesNotContain(app.Messages, entry => entry.Kind == HelloAccountMessageKind.PhoneSignInVerification);
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

    [Fact]
    public async Task DelegatedRegistrationRequiresActorAndProofAndNeverSwitchesSession()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        await using var app = await TestApplication.CreateAsync(postgres.GetConnectionString(),
            configureHello: options => options.PhoneOtpEnabled = true,
            configureServices: services => services.AddSingleton<IHelloDelegatedPhoneRegistrationPolicy<IntegrationProfile>, AllowDelegatedRegistration>());
        using var client = app.CreateClient(allowAutoRedirect: false);
        const string password = "correct horse battery staple";
        foreach (var name in new[] { "parent", "other-parent" })
        {
            using var registered = await client.PostAsJsonAsync("/auth/register", new { userName = name, email = name + "@example.test", phone = (string?)null, profile = new { displayName = name, locale = "en" }, password });
            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        }
        using var occupied = await client.PostAsJsonAsync("/auth/register", new { userName = "owner", email = "owner@example.test", phone = "+79991234586", profile = new { displayName = "Owner", locale = "en" }, password });
        Assert.Equal(HttpStatusCode.Created, occupied.StatusCode);
        var parent = await LoginAsync(client, "parent@example.test", password);
        var other = await LoginAsync(client, "other-parent@example.test", password);
        using var initial = await GetMeAsync(client, parent.AccessToken);
        var parentId = initial.RootElement.GetProperty("id").GetGuid();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IHelloDelegatedPhoneRegistration<IntegrationProfile>>();
        var begun = await service.BeginAsync(parent.AccessToken, "+79991234585", "parent-client", CancellationToken.None);
        Assert.True(begun.IsSuccess);
        var sms = await app.WaitForMessageAsync(HelloAccountMessageKind.PhoneSignInVerification);
        var child = new IntegrationProfile("Child", "en");
        var stolen = await service.CompleteAsync(other.AccessToken, begun.Value.Ticket, sms.VerificationCode!, "parent-client", child, false, false, CancellationToken.None);
        Assert.False(stolen.IsSuccess);
        var wrong = await service.CompleteAsync(parent.AccessToken, begun.Value.Ticket, sms.VerificationCode == "000000" ? "111111" : "000000", "parent-client", child, false, false, CancellationToken.None);
        Assert.False(wrong.IsSuccess);
        Assert.DoesNotContain(wrong.Errors, error => error.Code == IdentityErrorCodes.DuplicatePhone);
        var created = await service.CompleteAsync(parent.AccessToken, begun.Value.Ticket, sms.VerificationCode!, "parent-client", child, false, false, CancellationToken.None);
        Assert.True(created.IsSuccess);
        Assert.Equal("+79991234585", created.Value.Phone);
        Assert.True(created.Value.PhoneConfirmed);
        Assert.NotEqual(parentId, created.Value.Id);
        var replay = await service.CompleteAsync(parent.AccessToken, begun.Value.Ticket, sms.VerificationCode!, "parent-client", child, false, false, CancellationToken.None);
        Assert.False(replay.IsSuccess);
        var occupiedStart = await service.BeginAsync(parent.AccessToken, "+79991234586", "parent-client", CancellationToken.None);
        Assert.True(occupiedStart.IsSuccess);
        var occupiedSms = app.Messages.Last(entry => entry.Kind == HelloAccountMessageKind.PhoneSignInVerification);
        var occupiedWrong = await service.CompleteAsync(parent.AccessToken, occupiedStart.Value.Ticket,
            occupiedSms.VerificationCode == "000000" ? "111111" : "000000", "parent-client", child, false, false, CancellationToken.None);
        Assert.False(occupiedWrong.IsSuccess);
        Assert.DoesNotContain(occupiedWrong.Errors, error => error.Code == IdentityErrorCodes.DuplicatePhone);
        var occupiedCorrect = await service.CompleteAsync(parent.AccessToken, occupiedStart.Value.Ticket, occupiedSms.VerificationCode!, "parent-client", child, false, false, CancellationToken.None);
        Assert.False(occupiedCorrect.IsSuccess);
        Assert.Contains(occupiedCorrect.Errors, error => error.Code == IdentityErrorCodes.DuplicatePhone);
        using var stillParent = await GetMeAsync(client, parent.AccessToken);
        Assert.Equal(parentId, stillParent.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task HostSmsSharesGlobalBudgetWithPhoneOtp()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        await using var app = await TestApplication.CreateAsync(postgres.GetConnectionString(),
            configureHello: options => { options.PhoneOtpEnabled = true; options.DailySmsBudget = 1; });
        using var scope = app.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<IHelloSmsSender>();
        var sent = await sender.SendAsync("+79991234587", "Consent: https://example.test/consent", "client", CancellationToken.None);
        Assert.True(sent.IsSuccess);
        Assert.Contains(app.Messages, entry => entry.Kind == HelloAccountMessageKind.HostSms && entry.Text == "Consent: https://example.test/consent");
        using var client = app.CreateClient(allowAutoRedirect: false);
        using var otp = await client.PostAsJsonAsync("/auth/phone/request", new { phone = "+79991234588" });
        Assert.False(otp.IsSuccessStatusCode);
        Assert.DoesNotContain(app.Messages, entry => entry.Kind == HelloAccountMessageKind.PhoneSignInVerification);
    }

    [Fact]
    public async Task SiteSpecificLoginUiHidesMethodsAndRejectsDirectPosts()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        await using var app = await TestApplication.CreateAsync(postgres.GetConnectionString(), uiPathPrefix: "/identity",
            nativeBrowser: true,
            configureHello: options =>
            {
                options.PhoneOtpEnabled = true;
                options.Sites.Add(new HelloSiteOptions { Id = "staff", PublicOrigin = new Uri("https://staff.example.test"), PhoneOtpEnabled = false, SelfRegistrationEnabled = false, EnabledFeatures = HelloSiteFeatures.Login, ApplicationHomeUrl = "/staff-home", AuthenticatedRedirectPath = "/staff-home" });
                options.Sites.Add(new HelloSiteOptions { Id = "main", PublicOrigin = new Uri("https://main.example.test"), PasswordSignInEnabled = false });
            });
        using var staff = app.CreateClient(allowAutoRedirect: false);
        staff.DefaultRequestHeaders.Host = "staff.example.test";
        using var staffPage = await staff.GetAsync("/identity/login");
        Assert.Equal(HttpStatusCode.OK, staffPage.StatusCode);
        var html = await staffPage.Content.ReadAsStringAsync();
        Assert.DoesNotContain("name=\"Phone.PhoneNumber\"", html);
        Assert.DoesNotContain("href=\"/identity/register", html);
        Assert.Contains("/staff-home", html);
        using var registration = await staff.GetAsync("/identity/register");
        Assert.Equal(HttpStatusCode.NotFound, registration.StatusCode);
        var csrf = ReadInputValue(html, "__RequestVerificationToken");
        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);
        MergeCookies(cookies, staffPage);
        using var phonePost = await SendFormAsync(staff, "/identity/login?handler=PhoneRequest", cookies,
            new Dictionary<string, string> { ["Phone.PhoneNumber"] = "+79991234589", ["__RequestVerificationToken"] = csrf });
        Assert.Equal(HttpStatusCode.NotFound, phonePost.StatusCode);
        using var main = app.CreateClient(allowAutoRedirect: false);
        main.DefaultRequestHeaders.Host = "main.example.test";
        using var mainPage = await main.GetAsync("/identity/login");
        var mainHtml = await mainPage.Content.ReadAsStringAsync();
        Assert.Contains("name=\"Phone.PhoneNumber\"", mainHtml);
        Assert.DoesNotContain("name=\"Input.Password\"", mainHtml);
    }

    [Fact]
    public async Task AccessLinkUiInspectsWithoutConsumptionAndCompletesOnce()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        await using var app = await TestApplication.CreateAsync(postgres.GetConnectionString(), uiPathPrefix: "/identity", accessLinksEnabled: true);
        using var client = app.CreateClient(allowAutoRedirect: false);
        using var registered = await client.PostAsJsonAsync("/auth/register", new { userName = "access-link-user", email = "access-link@example.test", phone = (string?)null, profile = new { displayName = "Link user", locale = "en" }, password = "correct horse battery staple" });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        using var registration = JsonDocument.Parse(await registered.Content.ReadAsStringAsync());
        var userId = registration.RootElement.GetProperty("id").GetGuid();
        var version = registration.RootElement.GetProperty("version").GetInt64();
        using var scope = app.Services.CreateScope();
        var links = scope.ServiceProvider.GetRequiredService<IHelloAccessLinks<IntegrationProfile>>();
        var issued = await links.IssueAsync(userId, version, "default", false, CancellationToken.None);
        Assert.True(issued.IsSuccess);
        Assert.Equal("integration.skopka.test", issued.Value.Url.Host);
        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);
        using var page = await SendAsync(client, HttpMethod.Get, issued.Value.Url.PathAndQuery, cookies);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        MergeCookies(cookies, page);
        Assert.Equal("no-referrer", page.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("no-store", page.Headers.CacheControl?.ToString());
        var html = await page.Content.ReadAsStringAsync();
        var csrf = ReadInputValue(html, "__RequestVerificationToken");
        Assert.Empty(issued.Value.Url.Query);
        var token = Uri.UnescapeDataString(issued.Value.Url.Fragment["#token=".Length..]);
        using var inspect = await SendFormAsync(client, "/identity/access-link?handler=Inspect", cookies,
            new Dictionary<string, string> { ["Token"] = token, ["__RequestVerificationToken"] = csrf });
        Assert.Equal("/identity/access-link", inspect.Headers.Location?.OriginalString);
        using var inspectedPage = await FollowRedirectAsync(client, inspect, cookies);
        Assert.Contains("Set a password", await inspectedPage.Content.ReadAsStringAsync());
        using var secondInspect = await SendFormAsync(client, "/identity/access-link?handler=Inspect", cookies,
            new Dictionary<string, string> { ["Token"] = token, ["__RequestVerificationToken"] = csrf });
        using var secondInspectedPage = await FollowRedirectAsync(client, secondInspect, cookies);
        Assert.Contains("Set a password", await secondInspectedPage.Content.ReadAsStringAsync());
        using var begun = await SendFormAsync(client, "/identity/access-link?handler=Begin", cookies,
            new Dictionary<string, string> { ["Token"] = token, ["__RequestVerificationToken"] = csrf });
        using var enrollmentPage = await FollowRedirectAsync(client, begun, cookies);
        var enrollmentHtml = await enrollmentPage.Content.ReadAsStringAsync();
        var ticket = ReadInputValue(enrollmentHtml, "EnrollmentTicket");
        csrf = ReadInputValue(enrollmentHtml, "__RequestVerificationToken");
        var secret = System.Text.RegularExpressions.Regex.Match(enrollmentHtml, "<code>([A-Z2-7]+)</code>").Groups[1].Value;
        Assert.NotEmpty(secret);
        using var completed = await SendFormAsync(client, "/identity/access-link?handler=Complete", cookies,
            new Dictionary<string, string> { ["Token"] = token, ["EnrollmentTicket"] = ticket, ["Password"] = "another strong password 123", ["ConfirmPassword"] = "another strong password 123", ["Code"] = ComputeTotp(secret), ["__RequestVerificationToken"] = csrf });
        using var completedPage = await FollowRedirectAsync(client, completed, cookies);
        Assert.Contains("recovery codes", (await completedPage.Content.ReadAsStringAsync()), StringComparison.OrdinalIgnoreCase);
        using var replay = await SendFormAsync(client, "/identity/access-link?handler=Inspect", cookies,
            new Dictionary<string, string> { ["Token"] = token, ["__RequestVerificationToken"] = csrf });
        using var replayPage = await FollowRedirectAsync(client, replay, cookies);
        Assert.Contains("invalid, expired or already used", await replayPage.Content.ReadAsStringAsync());
    }

    private sealed class AllowDelegatedRegistration : IHelloDelegatedPhoneRegistrationPolicy<IntegrationProfile>
    {
        public Task<OperationResult> AuthorizeAsync(HelloAccount<IntegrationProfile> actor, CancellationToken cancellationToken)
            => Task.FromResult(OperationResultFactory.Success());
    }

}
