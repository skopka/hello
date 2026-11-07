using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello;
using Skopka.Identity.Users;
using Testcontainers.PostgreSql;
using Xunit;

namespace Skopka.Hello.IntegrationTests;

public sealed partial class AuthenticationFlowTests
{
    private sealed class StaffAccountPolicy : IHelloAccountPolicy<IntegrationProfile>
    {
        public Task<OperationResult<HelloAccountAccess>> GetPermissionAsync(IdentityUser<IntegrationProfile> user, HelloAccountArea area, CancellationToken ct)
            => Task.FromResult(OperationResultFactory.Success(user.Profile.DisplayName == "staff" && area is HelloAccountArea.Password or HelloAccountArea.UserName or HelloAccountArea.Phone or HelloAccountArea.Profile
                ? new HelloAccountAccess(false, false) : HelloAccountAccess.Allow));
        public Task<OperationResult> CheckProfileFieldsAsync(IdentityUser<IntegrationProfile> user, IReadOnlyCollection<string> fields, CancellationToken ct)
            => Task.FromResult(OperationResultFactory.Success());
        public Task<OperationResult> CheckProfileChangeAsync(IdentityUser<IntegrationProfile> user, IntegrationProfile profile, CancellationToken ct)
            => Task.FromResult(OperationResultFactory.Success());
    }

    [Fact]
    public async Task AccountPolicyDeniesForgedMutationsAndVerifiedReset()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        await using var app = await TestApplication.CreateAsync(postgres.GetConnectionString(), uiPathPrefix: "/identity",
            configureHello: options => options.PhoneOtpEnabled = true,
            configureServices: services => services.AddSingleton<IHelloAccountPolicy<IntegrationProfile>, StaffAccountPolicy>());
        using var client = app.CreateClient(allowAutoRedirect: false);
        const string password = "correct horse battery staple";
        using var registration = await client.PostAsJsonAsync("/auth/register", new { userName = "policy-staff", email = "policy-staff@example.test", phone = (string?)null, profile = new { displayName = "staff", locale = "en" }, password });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var login = await LoginAsync(client, "policy-staff@example.test", password);
        var cookies = await LoginThroughUiAsync(client, "policy-staff@example.test", password, null, "/identity/account");
        using var accountPage = await SendAsync(client, HttpMethod.Get, "/identity/account", cookies);
        var accountHtml = await accountPage.Content.ReadAsStringAsync();
        Assert.DoesNotContain("id=\"phone-binding\"", accountHtml);
        Assert.DoesNotContain("name=\"UserNameInput.UserName\"", accountHtml);
        using var security = await SendAsync(client, HttpMethod.Get, "/identity/account/security", cookies);
        Assert.DoesNotContain("handler=BeginSet", await security.Content.ReadAsStringAsync());
        using var initial = await GetMeAsync(client, login.AccessToken);
        var version = initial.RootElement.GetProperty("version").GetInt64();
        using var username = await SendAuthorizedJsonAsync(client, HttpMethod.Put, "/account/user-name", login.AccessToken, new { expectedVersion = version, userName = "forged" });
        Assert.Equal(HttpStatusCode.Forbidden, username.StatusCode);
        using var profile = await SendAuthorizedJsonAsync(client, HttpMethod.Put, "/account/profile", login.AccessToken, new { expectedVersion = version, profile = new { displayName = "changed", locale = "en" } });
        Assert.Equal(HttpStatusCode.Forbidden, profile.StatusCode);
        using var phone = await SendAuthorizedJsonAsync(client, HttpMethod.Post, "/account/phone/binding", login.AccessToken, new { phone = "+79991234589" });
        Assert.Equal(HttpStatusCode.Forbidden, phone.StatusCode);
        using var request = await client.PostAsJsonAsync("/auth/password-reset/request", new { email = "policy-staff@example.test" });
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var message = await app.WaitForMessageAsync(HelloAccountMessageKind.PasswordReset);
        var query = QueryHelpers.ParseQuery(Assert.IsType<Uri>(message.ActionUrl).Query);
        using var reset = await client.PostAsJsonAsync("/auth/password-reset/confirm", new { userId = Guid.Parse(query["userId"].Single()!), token = query["token"].Single(), newPassword = "new correct horse battery staple" });
        Assert.Equal(HttpStatusCode.Forbidden, reset.StatusCode);
        _ = await LoginAsync(client, "policy-staff@example.test", password);
    }
}
