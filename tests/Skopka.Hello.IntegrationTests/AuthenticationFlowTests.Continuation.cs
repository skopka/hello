using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello;
using Testcontainers.PostgreSql;
using Xunit;

namespace Skopka.Hello.IntegrationTests;

public sealed partial class AuthenticationFlowTests
{
    private sealed class TestOnboarding : IHelloRegistrationContinuation<IntegrationProfile>
    {
        private readonly ConcurrentDictionary<Guid, byte> completed = new();
        public Task<OperationResult<IntegrationProfile>> PrepareProfileAsync(IntegrationProfile profile, HelloRegistrationKind kind, CancellationToken ct)
            => Task.FromResult(OperationResultFactory.Success(profile with { DisplayName = "pending" }));
        public Task<OperationResult<bool>> IsPendingAsync(HelloAccount<IntegrationProfile> account, CancellationToken ct)
            => Task.FromResult(OperationResultFactory.Success(account.Profile.DisplayName == "pending" && !completed.ContainsKey(account.Id)));
        public void Complete(Guid id) => completed[id] = 1;
    }

    [Fact]
    public async Task PhoneRegistrationMustCompleteHostOnboardingBeforeOriginalPurchase()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        var gateOptions = new HelloContinuationGateOptions { ContinuationPath = "/onboarding" };
        await using var app = await TestApplication.CreateAsync(postgres.GetConnectionString(), nativeBrowser: true,
            configureHello: options => options.PhoneOtpEnabled = true,
            configureServices: services => services.AddSingleton<IHelloRegistrationContinuation<IntegrationProfile>, TestOnboarding>(),
            configurePipeline: web => web.UseHelloRegistrationContinuation<IntegrationProfile>(gateOptions),
            configureEndpoints: web =>
            {
                web.MapGet("/purchase", () => Results.Text("purchase")).RequireAuthorization();
                web.MapGet("/onboarding", (HttpContext http, IAntiforgery antiforgery) =>
                {
                    var tokens = antiforgery.GetAndStoreTokens(http);
                    return Results.Content($"<form method=\"post\" action=\"/onboarding/complete\"><input name=\"__RequestVerificationToken\" value=\"{tokens.RequestToken}\" /></form>", "text/html");
                }).RequireAuthorization().WithMetadata(new HelloContinuationExemptAttribute());
                web.MapPost("/onboarding/complete", async (HttpContext http, IAntiforgery antiforgery,
                    IHelloIdentityApplication<IntegrationProfile> identity, IHelloRegistrationContinuation<IntegrationProfile> workflow,
                    IDataProtectionProvider protection, CancellationToken ct) =>
                {
                    await antiforgery.ValidateRequestAsync(http);
                    var auth = await http.AuthenticateAsync("Skopka.Hello.UI");
                    var token = auth.Properties?.GetTokenValue("access_token");
                    if (token is null) return Results.Unauthorized();
                    var user = await identity.ValidateAccessTokenAsync(token, ct);
                    if (!user.IsSuccess) return Results.Unauthorized();
                    ((TestOnboarding)workflow).Complete(user.Value.Id);
                    var destination = await HelloContinuationCompletion.CompleteAsync(http, identity, workflow, protection, gateOptions, ct);
                    return destination.IsSuccess ? Results.LocalRedirect(destination.Value) : Results.Forbid();
                }).RequireAuthorization().WithMetadata(new HelloContinuationExemptAttribute());
            });
        using var client = app.CreateClient(allowAutoRedirect: false);
        Dictionary<string, string> cookies = new(StringComparer.Ordinal);
        using var page = await SendAsync(client, HttpMethod.Get, "/hello/login?ReturnUrl=%2Fpurchase%3Fitem%3D1", cookies);
        MergeCookies(cookies, page);
        var csrf = ReadInputValue(await page.Content.ReadAsStringAsync(), "__RequestVerificationToken");
        using var requested = await SendFormAsync(client, "/hello/login?handler=PhoneRequest", cookies,
            new Dictionary<string, string> { ["Phone.PhoneNumber"] = "+79991234594", ["ReturnUrl"] = "/purchase?item=1", ["__RequestVerificationToken"] = csrf });
        var html = await requested.Content.ReadAsStringAsync();
        var requestId = ReadInputValue(html, "Phone.RequestId");
        csrf = ReadInputValue(html, "__RequestVerificationToken");
        var sms = await app.WaitForMessageAsync(HelloAccountMessageKind.PhoneSignInVerification);
        using var verified = await SendFormAsync(client, "/hello/login?handler=PhoneVerify", cookies,
            new Dictionary<string, string> { ["Phone.RequestId"] = requestId, ["Phone.Code"] = sms.VerificationCode!, ["ReturnUrl"] = "/purchase?item=1", ["__RequestVerificationToken"] = csrf });
        html = await verified.Content.ReadAsStringAsync();
        var registrationToken = ReadInputValue(html, "Phone.RegistrationToken");
        csrf = ReadInputValue(html, "__RequestVerificationToken");
        using var registered = await SendFormAsync(client, "/hello/login?handler=PhoneRegister", cookies,
            new Dictionary<string, string> { ["Phone.RegistrationToken"] = registrationToken, ["Phone.DisplayName"] = "member", ["ReturnUrl"] = "/purchase?item=1", ["__RequestVerificationToken"] = csrf });
        Assert.Equal(HttpStatusCode.Redirect, registered.StatusCode);
        MergeCookies(cookies, registered);
        using var direct = new HttpRequestMessage(HttpMethod.Get, "/purchase?item=1");
        direct.Headers.Accept.ParseAdd("text/html");
        direct.Headers.Add("Cookie", string.Join("; ", cookies.Select(x => x.Key + "=" + x.Value)));
        using var blocked = await client.SendAsync(direct);
        Assert.Equal("/onboarding", blocked.Headers.Location?.ToString());
        MergeCookies(cookies, blocked);
        using var api = new HttpRequestMessage(HttpMethod.Get, "/account/me");
        api.Headers.Accept.ParseAdd("application/json");
        api.Headers.Add("Cookie", string.Join("; ", cookies.Select(x => x.Key + "=" + x.Value)));
        using var apiBlocked = await client.SendAsync(api);
        Assert.Equal(HttpStatusCode.Forbidden, apiBlocked.StatusCode);
        using var onboard = await SendAsync(client, HttpMethod.Get, "/onboarding", cookies);
        MergeCookies(cookies, onboard);
        csrf = ReadInputValue(await onboard.Content.ReadAsStringAsync(), "__RequestVerificationToken");
        using var done = await SendFormAsync(client, "/onboarding/complete", cookies, new Dictionary<string, string> { ["__RequestVerificationToken"] = csrf });
        Assert.Equal("/purchase?item=1", done.Headers.Location?.ToString());
        MergeCookies(cookies, done);
        using var purchase = await SendAsync(client, HttpMethod.Get, "/purchase?item=1", cookies);
        Assert.Equal(HttpStatusCode.OK, purchase.StatusCode);
    }
}
