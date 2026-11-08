using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
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
                // The scenario registers more accounts from one client than the default limit allows.
                options.RegistrationClientPermitLimit = 20;
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
        using var personalRegistration = await main.PostAsJsonAsync("/auth/register", new
        {
            userName = "scoped-personal",
            email = "scoped-personal@example.test",
            phone = personalOnly,
            profile = new { displayName = "Scoped personal", locale = "en" },
            password,
        });
        Assert.Equal(HttpStatusCode.Created, personalRegistration.StatusCode);
        using (var registered = JsonDocument.Parse(await personalRegistration.Content.ReadAsStringAsync()))
        {
            await ConfirmScopedPhoneAsync(main, app, personalOnly,
                registered.RootElement.GetProperty("id").GetGuid());
        }

        var second = (await LookupAsync(app, personalOnly, null)).Id;
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

        // A queued phone confirmation finds the account in the scope of the site that accepted it.
        const string third = "+79991234603";
        await RegisterByPhoneAsync(main, app, third, "Third personal");
        using var unconfirmedStaff = await staff.PostAsJsonAsync("/auth/register", new
        {
            userName = "unconfirmed-staff",
            email = "unconfirmed-staff@example.test",
            phone = third,
            profile = new { displayName = "Unconfirmed staff", locale = "en" },
            password,
        });
        Assert.True(unconfirmedStaff.StatusCode == HttpStatusCode.Created, await unconfirmedStaff.Content.ReadAsStringAsync());
        var unconfirmed = await LookupAsync(app, third, "staff");
        Assert.False(unconfirmed.PhoneConfirmed);
        await ConfirmScopedPhoneAsync(staff, app, third, unconfirmed.Id);
        Assert.True((await LookupAsync(app, third, "staff")).PhoneConfirmed);

        // Automatic password sign-in limits an entered number to the site's scope, so the
        // shared number is not ambiguous; the main site passes the default scope explicitly.
        var automaticMain = await LoginAsync(main, personalOnly, password);
        using var automaticMainMe = await GetMeAsync(main, automaticMain.AccessToken);
        Assert.Equal("scoped-personal", automaticMainMe.RootElement.GetProperty("userName").GetString());
        var automaticStaff = await LoginAsync(staff, personalOnly, password);
        using var automaticStaffMe = await GetMeAsync(staff, automaticStaff.AccessToken);
        Assert.Equal("scoped-staff", automaticStaffMe.RootElement.GetProperty("userName").GetString());
    }

    private static async Task<IdentityUser<IntegrationProfile>> LookupAsync(TestApplication app, string phone, string? scope)
    {
        using var lookupScope = app.Services.CreateScope();
        var lookup = lookupScope.ServiceProvider.GetRequiredService<IIdentityUserLookupService<IntegrationProfile>>();
        var found = await lookup.FindActiveByPhoneAsync(phone, scope, CancellationToken.None);
        Assert.True(found.IsSuccess);
        return found.Value;
    }

    // Requests and confirms the phone of the account the confirmation link names.
    private static async Task ConfirmScopedPhoneAsync(HttpClient client, TestApplication app, string phone, Guid userId)
    {
        using var requested = await client.PostAsJsonAsync("/auth/phone-confirmation/request", new { phone });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        HelloAccountMessage? message = null;
        while (message is null)
        {
            message = app.Messages.LastOrDefault(item => item.Kind == HelloAccountMessageKind.PhoneConfirmation
                && item.RecipientAddress == phone);
            if (message is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
            }
        }

        var query = QueryHelpers.ParseQuery(Assert.IsType<Uri>(message.ActionUrl).Query);
        Assert.Equal(userId, Guid.Parse(query["userId"].Single()!));
        using var confirmed = await client.PostAsJsonAsync("/auth/phone-confirmation/confirm",
            new { userId, phone = query["phone"].Single(), token = query["token"].Single() });
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
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
