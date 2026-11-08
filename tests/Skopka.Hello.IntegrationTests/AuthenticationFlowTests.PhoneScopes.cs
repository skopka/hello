using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Hello;
using Skopka.Hello.Endpoints;
using Skopka.Identity.Authentication;
using Skopka.Identity.Users;
using Testcontainers.PostgreSql;
using Xunit;

namespace Skopka.Hello.IntegrationTests;

public sealed partial class AuthenticationFlowTests
{
    [Fact]
    public async Task SitePhoneScopesKeepOneNumberPerSiteAccount()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        await using var app = await TestApplication.CreateAsync(postgres.GetConnectionString(),
            configureHello: options =>
            {
                options.PhoneOtpEnabled = true;
                options.DefaultPhoneRegion = "RU";
                options.Sites.Add(new HelloSiteOptions { Id = "main", PublicOrigin = new Uri("https://main.example.test") });
                options.Sites.Add(new HelloSiteOptions
                {
                    Id = "staff",
                    PublicOrigin = new Uri("https://staff.example.test"),
                    PhoneScope = "staff",
                });
                options.Sites.Add(new HelloSiteOptions
                {
                    Id = "desk",
                    PublicOrigin = new Uri("https://desk.example.test"),
                    PhoneScope = "staff",
                    PasswordLoginHandle = PasswordLoginHandle.Phone,
                });
            },
            shortVerificationCooldown: true);
        using var main = app.CreateClient(allowAutoRedirect: false);
        main.DefaultRequestHeaders.Host = "main.example.test";
        using var staff = app.CreateClient(allowAutoRedirect: false);
        staff.DefaultRequestHeaders.Host = "staff.example.test";
        const string shared = "+79991234601";

        // The same number registers once per site: personal in the default scope, staff in "staff".
        var personal = await RegisterByPhoneAsync(main, app, shared, "Personal");
        var staffAccount = await RegisterByPhoneAsync(staff, app, shared, "Staff");
        Assert.NotEqual(personal, staffAccount);
        using (var scope = app.Services.CreateScope())
        {
            var lookup = scope.ServiceProvider.GetRequiredService<IIdentityUserLookupService<IntegrationProfile>>();
            var inDefault = await lookup.FindActiveByPhoneAsync(shared, CancellationToken.None);
            Assert.Equal(personal, inDefault.Value.Id);
            Assert.Equal(IdentityPhoneScopes.Default, inDefault.Value.PhoneScope);
            var inStaff = await lookup.FindActiveByPhoneAsync(shared, "staff", CancellationToken.None);
            Assert.Equal(staffAccount, inStaff.Value.Id);
            Assert.Equal("staff", inStaff.Value.PhoneScope);
        }

        // Code sign-in lands on the site's own account.
        Assert.Equal(personal, await SignInByPhoneAsync(main, app, shared));
        Assert.Equal(staffAccount, await SignInByPhoneAsync(staff, app, shared));

        // A staff account bound on the staff site may take a number a personal account already holds.
        const string password = "correct horse battery staple";
        const string personalOnly = "+79991234602";
        var second = await RegisterByPhoneAsync(main, app, personalOnly, "Second personal");
        using var staffRegistration = await staff.PostAsJsonAsync("/auth/register", new
        {
            userName = "scoped-staff",
            email = "scoped-staff@example.test",
            phone = (string?)null,
            profile = new { displayName = "Scoped staff", locale = "en" },
            password,
        });
        Assert.Equal(HttpStatusCode.Created, staffRegistration.StatusCode);
        var staffLogin = await LoginAsync(staff, "scoped-staff", password);
        var sentBefore = PhoneCodeCount(app, personalOnly);
        using var begun = await SendAuthorizedJsonAsync(staff, HttpMethod.Post, "/account/phone/binding",
            staffLogin.AccessToken, new { phone = personalOnly });
        Assert.Equal(HttpStatusCode.OK, begun.StatusCode);
        using var ticket = JsonDocument.Parse(await begun.Content.ReadAsStringAsync());
        var code = await NextPhoneCodeAsync(app, personalOnly, sentBefore);
        using var bound = await SendAuthorizedJsonAsync(staff, HttpMethod.Post, "/account/phone/binding/complete",
            staffLogin.AccessToken, new { ticket = ticket.RootElement.GetProperty("ticket").GetString(), code });
        Assert.Equal(HttpStatusCode.NoContent, bound.StatusCode);
        using var staffMe = await GetMeAsync(staff, staffLogin.AccessToken);
        Assert.Equal(personalOnly, staffMe.RootElement.GetProperty("phone").GetString());
        Assert.True(staffMe.RootElement.GetProperty("phoneConfirmed").GetBoolean());

        // Password sign-in with the phone handle resolves the account in the site's scope.
        using var desk = app.CreateClient(allowAutoRedirect: false);
        desk.DefaultRequestHeaders.Host = "desk.example.test";
        var byPhone = await LoginAsync(desk, personalOnly, password);
        using var byPhoneMe = await GetMeAsync(desk, byPhone.AccessToken);
        Assert.Equal("scoped-staff", byPhoneMe.RootElement.GetProperty("userName").GetString());
        Assert.Equal(second, await SignInByPhoneAsync(main, app, personalOnly));
    }

    private static int PhoneCodeCount(TestApplication app, string phone)
        => app.Messages.Count(message => message.Kind == HelloAccountMessageKind.PhoneSignInVerification
            && message.RecipientAddress == phone);

    // Waits for a code sent after <paramref name="before"/> codes to this number.
    private static async Task<string> NextPhoneCodeAsync(TestApplication app, string phone, int before)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (PhoneCodeCount(app, phone) <= before)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }

        return Assert.IsType<string>(app.Messages
            .Last(message => message.Kind == HelloAccountMessageKind.PhoneSignInVerification
                && message.RecipientAddress == phone).VerificationCode);
    }

    private static async Task<PhoneCodeVerifyResponse> VerifyPhoneCodeAsync(HttpClient client, TestApplication app, string phone)
    {
        var before = PhoneCodeCount(app, phone);
        using var request = await client.PostAsJsonAsync("/auth/phone/request", new { phone });
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var challenge = Assert.IsType<PhoneCodeRequestResponse>(await request.Content.ReadFromJsonAsync<PhoneCodeRequestResponse>());
        var code = await NextPhoneCodeAsync(app, phone, before);
        using var verified = await client.PostAsJsonAsync("/auth/phone/verify", new { requestId = challenge.RequestId, code });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        return Assert.IsType<PhoneCodeVerifyResponse>(await verified.Content.ReadFromJsonAsync<PhoneCodeVerifyResponse>(WebJsonOptions));
    }

    private static async Task<Guid> RegisterByPhoneAsync(HttpClient client, TestApplication app, string phone, string name)
    {
        var verified = await VerifyPhoneCodeAsync(client, app, phone);
        Assert.NotNull(verified.RegistrationToken);
        using var registration = await client.PostAsJsonAsync("/auth/phone/register", new
        {
            registrationToken = verified.RegistrationToken,
            profile = new { displayName = name, locale = "ru" },
            acceptTermsOfService = true,
            acceptPrivacyPolicy = true,
        });
        Assert.True(registration.IsSuccessStatusCode, await registration.Content.ReadAsStringAsync());
        var session = Assert.IsType<SessionResponse>(await registration.Content.ReadFromJsonAsync<SessionResponse>(WebJsonOptions));
        using var me = await GetMeAsync(client, session.AccessToken);
        return me.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> SignInByPhoneAsync(HttpClient client, TestApplication app, string phone)
    {
        var verified = await VerifyPhoneCodeAsync(client, app, phone);
        Assert.Null(verified.RegistrationToken);
        var session = Assert.IsType<SessionResponse>(verified.Session);
        using var me = await GetMeAsync(client, session.AccessToken);
        return me.RootElement.GetProperty("id").GetGuid();
    }
}
