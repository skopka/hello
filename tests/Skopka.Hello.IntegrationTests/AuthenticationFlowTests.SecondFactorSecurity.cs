using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Hello.Endpoints;
using Skopka.Identity.Users;
using Testcontainers.PostgreSql;

namespace Skopka.Hello.IntegrationTests;

public sealed partial class AuthenticationFlowTests
{
    [Fact]
    public async Task LiveMandatoryPolicyRejectsOldAccessAndRefreshAndGatesPasskeySignIn()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        var policy = new ToggleSecondFactorPolicy();
        await using var app = await TestApplication.CreateAsync(postgres.GetConnectionString(),
            configureHello: options => options.Totp.Enabled = true,
            passkeysEnabled: true,
            configureServices: services => services.AddSingleton<IHelloSignInSecondFactorPolicy<IntegrationProfile>>(policy));
        using var client = app.CreateClient(allowAutoRedirect: false);
        const string email = "policy-passkey@example.test";
        const string password = "correct horse battery staple";
        using var registered = await client.PostAsJsonAsync("/auth/register", new
        {
            userName = "policy-passkey",
            email,
            phone = (string?)null,
            profile = new { displayName = "Policy", locale = "en" },
            password,
        });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var existing = await LoginAsync(client, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", existing.AccessToken);
        using var start = await client.PostAsync("/account/passkeys/challenge", null);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var offer = await start.Content.ReadFromJsonAsync<WebAuthnRegistrationChallengeResponse>(WebJsonOptions);
        Assert.NotNull(offer);
        var authenticator = new SoftwareAuthenticator(offer.RelyingPartyId);
        using var added = await client.PostAsJsonAsync("/account/passkeys", new CompleteWebAuthnRegistrationRequest(
            offer.Ticket, Base64UrlTextEncoder.Encode(SoftwareAuthenticator.ClientData("webauthn.create", offer.Challenge)),
            Base64UrlTextEncoder.Encode(authenticator.Attestation()), "policy key"), WebJsonOptions);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        policy.Required = true;
        using var account = await client.GetAsync("/account/me");
        Assert.False(account.IsSuccessStatusCode);
        using var refreshRequest = CreateCookieMutation(HttpMethod.Post, "/auth/refresh", existing.Cookies);
        using var refresh = await client.SendAsync(refreshRequest);
        Assert.False(refresh.IsSuccessStatusCode);
        AssertNoSession(refresh);
        using var anonymous = app.CreateClient(allowAutoRedirect: false);
        using var challengeResponse = await anonymous.PostAsync("/auth/passkey/challenge", null);
        Assert.Equal(HttpStatusCode.OK, challengeResponse.StatusCode);
        var challenge = await challengeResponse.Content.ReadFromJsonAsync<WebAuthnAssertionChallengeResponse>(WebJsonOptions);
        Assert.NotNull(challenge);
        var clientData = SoftwareAuthenticator.ClientData("webauthn.get", challenge.Challenge);
        var assertion = authenticator.Assert(clientData);
        using var result = await anonymous.PostAsJsonAsync("/auth/passkey", new CompleteWebAuthnSignInRequest(
            challenge.Ticket, Base64UrlTextEncoder.Encode(authenticator.CredentialId),
            Base64UrlTextEncoder.Encode(clientData), Base64UrlTextEncoder.Encode(assertion.AuthenticatorData),
            Base64UrlTextEncoder.Encode(assertion.Signature)), WebJsonOptions);
        await AssertRequiresSecondFactor(result);
    }

    [Fact]
    public async Task PhoneRegistrationRequiresSecondFactorBeforeSession()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        await using var app = await TestApplication.CreateAsync(postgres.GetConnectionString(),
            configureHello: options => { options.PhoneOtpEnabled = true; options.DefaultPhoneRegion = "RU"; options.Totp.Enabled = true; },
            shortVerificationCooldown: true, requireSecondFactor: true);
        using var client = app.CreateClient(allowAutoRedirect: false);
        using var request = await client.PostAsJsonAsync("/auth/phone/request", new { phone = "8 999 123-45-72" });
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var challenge = await request.Content.ReadFromJsonAsync<PhoneCodeRequestResponse>();
        Assert.NotNull(challenge);
        var message = await app.WaitForMessageAsync(HelloAccountMessageKind.PhoneSignInVerification);
        using var verificationResponse = await client.PostAsJsonAsync("/auth/phone/verify", new { requestId = challenge.RequestId, code = message.VerificationCode });
        Assert.Equal(HttpStatusCode.OK, verificationResponse.StatusCode);
        var verification = await verificationResponse.Content.ReadFromJsonAsync<PhoneCodeVerifyResponse>();
        Assert.NotNull(verification);
        using var registration = await client.PostAsJsonAsync("/auth/phone/register", new
        {
            registrationToken = verification.RegistrationToken,
            profile = new { displayName = "Phone", locale = "ru" },
            acceptTermsOfService = false,
            acceptPrivacyPolicy = false,
        });
        await AssertRequiresSecondFactor(registration);
    }

    private static async Task AssertRequiresSecondFactor(HttpResponseMessage response)
    {
        Assert.False(response.IsSuccessStatusCode);
        AssertNoSession(response);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HelloSecondFactorErrorCodes.Required, problem.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(problem.RootElement.GetProperty("secondFactor").GetProperty("ticket").GetString()));
    }
    private static void AssertNoSession(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            Assert.DoesNotContain(cookies, value => value.StartsWith($"{RefreshCookieName}=", StringComparison.Ordinal));
    }
    private sealed class ToggleSecondFactorPolicy : IHelloSignInSecondFactorPolicy<IntegrationProfile>
    {
        public bool Required { get; set; }
        public Task<bool> RequiresAsync(IdentityUser<IntegrationProfile> user, CancellationToken cancellationToken) => Task.FromResult(Required);
    }
}
