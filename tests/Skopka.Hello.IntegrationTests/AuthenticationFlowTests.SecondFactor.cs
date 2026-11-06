using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Skopka.Identity.Users;
using Testcontainers.PostgreSql;

namespace Skopka.Hello.IntegrationTests;

public sealed partial class AuthenticationFlowTests
{
    private sealed class AlwaysRequireSecondFactor : IHelloSignInSecondFactorPolicy<IntegrationProfile>
    {
        public AlwaysRequireSecondFactor() { }

        public Task<bool> RequiresAsync(IdentityUser<IntegrationProfile> user, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    [Fact]
    public async Task PasswordRequiresEnrollmentAndRecoveryAcknowledgmentBeforeSession()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        await using var app = await TestApplication.CreateAsync(postgres.GetConnectionString(),
            configureHello: options => options.Totp.Enabled = true, requireSecondFactor: true);
        using var client = app.CreateClient(allowAutoRedirect: false);
        const string email = "required-factor@example.test";
        const string password = "correct horse battery staple";
        using var register = await client.PostAsJsonAsync("/auth/register", new
        {
            userName = "required-factor",
            email,
            phone = (string?)null,
            profile = new { displayName = "Required factor", locale = "en" },
            password,
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        using var login = await client.PostAsJsonAsync("/auth/login", new { login = email, password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.False(login.Headers.Contains("Set-Cookie"));
        using var problem = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var ticket = problem.RootElement.GetProperty("secondFactor").GetProperty("ticket").GetString();
        Assert.False(string.IsNullOrEmpty(ticket));
        using var begin = await client.PostAsJsonAsync("/auth/second-factor/enrollment", new { ticket });
        Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
        Assert.False(begin.Headers.Contains("Set-Cookie"));
        var enrollment = await begin.Content.ReadFromJsonAsync<HelloSecondFactorEnrollment>();
        Assert.NotNull(enrollment);
        Assert.StartsWith("otpauth://", enrollment.ProvisioningUri, StringComparison.Ordinal);
        var currentCode = ComputeTotp(enrollment.Secret);
        using var wrong = await client.PostAsJsonAsync("/auth/second-factor/enrollment/confirm", new
        {
            ticket = enrollment.Ticket,
            code = currentCode == "000000" ? "111111" : "000000",
        });
        Assert.False(wrong.IsSuccessStatusCode);
        Assert.False(wrong.Headers.Contains("Set-Cookie"));
        using var confirm = await client.PostAsJsonAsync("/auth/second-factor/enrollment/confirm", new
        {
            ticket = enrollment.Ticket,
            code = currentCode,
        });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.False(confirm.Headers.Contains("Set-Cookie"));
        var recovery = await confirm.Content.ReadFromJsonAsync<HelloSecondFactorRecovery>();
        Assert.NotNull(recovery);
        Assert.NotEmpty(recovery.RecoveryCodes);
        using var acknowledge = await client.PostAsJsonAsync("/auth/second-factor/enrollment/acknowledge", new { ticket = recovery.Ticket });
        Assert.Equal(HttpStatusCode.OK, acknowledge.StatusCode);
        Assert.True(acknowledge.Headers.Contains("Set-Cookie"));
        using var replay = await client.PostAsJsonAsync("/auth/second-factor/enrollment/acknowledge", new { ticket = recovery.Ticket });
        Assert.False(replay.IsSuccessStatusCode);
        using var nextLogin = await client.PostAsJsonAsync("/auth/login", new { login = email, password });
        Assert.Equal(HttpStatusCode.Unauthorized, nextLogin.StatusCode);
        using var nextProblem = JsonDocument.Parse(await nextLogin.Content.ReadAsStringAsync());
        var nextTicket = nextProblem.RootElement.GetProperty("secondFactor").GetProperty("ticket").GetString();
        using var verifyBegin = await client.PostAsJsonAsync("/auth/second-factor/verification", new { ticket = nextTicket });
        Assert.Equal(HttpStatusCode.OK, verifyBegin.StatusCode);
        var verify = await verifyBegin.Content.ReadFromJsonAsync<HelloSecondFactorContinuation>();
        Assert.NotNull(verify);
        using var completed = await client.PostAsJsonAsync("/auth/second-factor/verification/complete", new
        {
            ticket = verify.Ticket,
            code = recovery.RecoveryCodes[0],
        });
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.True(completed.Headers.Contains("Set-Cookie"));
    }
}
