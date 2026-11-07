using System.Net;
using System.Text.RegularExpressions;
using Testcontainers.PostgreSql;

namespace Skopka.Hello.IntegrationTests;

public sealed partial class AuthenticationFlowTests
{
    [Fact]
    public async Task PhoneRegistrationImmediatelyShowsReloadableSecondFactorEnrollment()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        await using var app = await TestApplication.CreateAsync(postgres.GetConnectionString(),
            configureHello: options => { options.PhoneOtpEnabled = true; options.DefaultPhoneRegion = "RU"; options.Totp.Enabled = true; },
            shortVerificationCooldown: true, requireSecondFactor: true);
        using var client = app.CreateClient(allowAutoRedirect: false);
        Dictionary<string, string> cookies = new(StringComparer.Ordinal);
        using var page = await SendAsync(client, HttpMethod.Get, "/hello/login", cookies);
        MergeCookies(cookies, page);
        var html = await page.Content.ReadAsStringAsync();
        using var requested = await SendFormAsync(client, "/hello/login?handler=PhoneRequest", cookies,
            new Dictionary<string, string> { ["Phone.PhoneNumber"] = "+79991234573", ["__RequestVerificationToken"] = ReadInputValue(html, "__RequestVerificationToken") });
        MergeCookies(cookies, requested);
        html = await requested.Content.ReadAsStringAsync();
        var message = await app.WaitForMessageAsync(HelloAccountMessageKind.PhoneSignInVerification);
        Assert.NotNull(message.VerificationCode);
        using var verified = await SendFormAsync(client, "/hello/login?handler=PhoneVerify", cookies,
            new Dictionary<string, string> { ["Phone.RequestId"] = ReadInputValue(html, "Phone.RequestId"), ["Phone.Code"] = message.VerificationCode, ["__RequestVerificationToken"] = ReadInputValue(html, "__RequestVerificationToken") });
        MergeCookies(cookies, verified);
        html = await verified.Content.ReadAsStringAsync();
        using var registered = await SendFormAsync(client, "/hello/login?handler=PhoneRegister", cookies,
            new Dictionary<string, string> { ["Phone.RegistrationToken"] = ReadInputValue(html, "Phone.RegistrationToken"), ["Phone.DisplayName"] = "Phone member", ["Phone.Locale"] = "en", ["__RequestVerificationToken"] = ReadInputValue(html, "__RequestVerificationToken") });
        Assert.Equal(HttpStatusCode.Redirect, registered.StatusCode);
        MergeCookies(cookies, registered);
        Assert.DoesNotContain(cookies.Keys, name => name.Contains("refresh", StringComparison.OrdinalIgnoreCase));
        using var enrollment = await SendAsync(client, HttpMethod.Get, registered.Headers.Location!.ToString(), cookies);
        MergeCookies(cookies, enrollment);
        html = await enrollment.Content.ReadAsStringAsync();
        Assert.Contains("name=\"SecondFactorCode\"", html);
        Assert.Contains("hello-qr-code", html);
        Assert.DoesNotContain("SecondFactorStart", html);
        var ticket = ReadInputValue(html, "SecondFactorTicket");
        using var reload = await SendAsync(client, HttpMethod.Get, "/hello/login", cookies);
        MergeCookies(cookies, reload);
        var reloadedHtml = await reload.Content.ReadAsStringAsync();
        Assert.Contains("name=\"SecondFactorCode\"", reloadedHtml);
        Assert.Contains("hello-qr-code", reloadedHtml);
        Assert.Equal(ticket, ReadInputValue(reloadedHtml, "SecondFactorTicket"));

        var originalSecret = Regex.Match(reloadedHtml, "<code>([^<]+)</code>").Groups[1].Value;
        Assert.NotEmpty(originalSecret);
        var originalCsrf = ReadInputValue(reloadedHtml, "__RequestVerificationToken");
        using var mismatch = await SendFormAsync(client, "/hello/login?handler=SecondFactorVerify", cookies,
            new Dictionary<string, string> { ["SecondFactorTicket"] = ticket + "invalid", ["SecondFactorCode"] = ComputeTotp(originalSecret), ["mode"] = "enroll", ["__RequestVerificationToken"] = originalCsrf });
        AssertNoSession(mismatch);
        MergeCookies(cookies, mismatch);
        Assert.DoesNotContain("hello-qr-code", await mismatch.Content.ReadAsStringAsync());

        // Restart after registration; the newly created account can now sign in by phone.
        using var restart = await SendFormAsync(client, "/hello/login?handler=SecondFactorRestart", cookies,
            new Dictionary<string, string> { ["__RequestVerificationToken"] = ReadInputValue(reloadedHtml, "__RequestVerificationToken") });
        MergeCookies(cookies, restart);
        using var staleEnrollment = await SendFormAsync(client, "/hello/login?handler=SecondFactorVerify", cookies,
            new Dictionary<string, string> { ["SecondFactorTicket"] = ticket, ["SecondFactorCode"] = ComputeTotp(originalSecret), ["mode"] = "enroll", ["__RequestVerificationToken"] = originalCsrf });
        AssertNoSession(staleEnrollment);
        MergeCookies(cookies, staleEnrollment);
        Assert.DoesNotContain("hello-qr-code", await staleEnrollment.Content.ReadAsStringAsync());
        var phoneEnrollment = await BeginPhoneSignInAsync(client, app, cookies);
        Assert.Contains("hello-qr-code", phoneEnrollment);
        Assert.Contains("name=\"SecondFactorCode\"", phoneEnrollment);
        var secret = Regex.Match(phoneEnrollment, "<code>([^<]+)</code>").Groups[1].Value;
        Assert.NotEmpty(secret);
        using var confirm = await SendFormAsync(client, "/hello/login?handler=SecondFactorVerify", cookies,
            new Dictionary<string, string> { ["SecondFactorTicket"] = ReadInputValue(phoneEnrollment, "SecondFactorTicket"), ["SecondFactorCode"] = ComputeTotp(secret), ["mode"] = "enroll", ["__RequestVerificationToken"] = ReadInputValue(phoneEnrollment, "__RequestVerificationToken") });
        Assert.Equal(HttpStatusCode.Redirect, confirm.StatusCode);
        MergeCookies(cookies, confirm);
        Assert.DoesNotContain(cookies.Keys, name => name.Contains("refresh", StringComparison.OrdinalIgnoreCase));
        using var recovery = await SendAsync(client, HttpMethod.Get, "/hello/login", cookies);
        MergeCookies(cookies, recovery);
        var recoveryHtml = await recovery.Content.ReadAsStringAsync();
        Assert.Contains("SecondFactorAcknowledge", recoveryHtml);
        var recoveryCode = Regex.Match(recoveryHtml, "<li><code>([^<]+)</code></li>").Groups[1].Value;
        Assert.NotEmpty(recoveryCode);
        var beforeAcknowledge = new Dictionary<string, string>(cookies, StringComparer.Ordinal);
        using var acknowledge = await SendFormAsync(client, "/hello/login?handler=SecondFactorAcknowledge", cookies,
            new Dictionary<string, string> { ["SecondFactorTicket"] = ReadInputValue(recoveryHtml, "SecondFactorTicket"), ["__RequestVerificationToken"] = ReadInputValue(recoveryHtml, "__RequestVerificationToken") });
        MergeCookies(cookies, acknowledge);
        Assert.Contains(cookies.Keys, name => name.Contains("refresh", StringComparison.OrdinalIgnoreCase));
        using var replayAcknowledge = await SendFormAsync(client, "/hello/login?handler=SecondFactorAcknowledge", beforeAcknowledge,
            new Dictionary<string, string> { ["SecondFactorTicket"] = ReadInputValue(recoveryHtml, "SecondFactorTicket"), ["__RequestVerificationToken"] = ReadInputValue(recoveryHtml, "__RequestVerificationToken") });
        AssertNoSession(replayAcknowledge);

        Dictionary<string, string> freshCookies = new(StringComparer.Ordinal);
        var phoneVerification = await BeginPhoneSignInAsync(client, app, freshCookies);
        Assert.Contains("name=\"SecondFactorCode\"", phoneVerification);
        Assert.DoesNotContain("hello-qr-code", phoneVerification);
        Assert.DoesNotContain("SecondFactorStart", phoneVerification);
        Assert.DoesNotContain(freshCookies.Keys, name => name.Contains("refresh", StringComparison.OrdinalIgnoreCase));
        var verificationTicket = ReadInputValue(phoneVerification, "SecondFactorTicket");
        using var verificationReload = await SendAsync(client, HttpMethod.Get, "/hello/login", freshCookies);
        MergeCookies(freshCookies, verificationReload);
        var verificationHtml = await verificationReload.Content.ReadAsStringAsync();
        Assert.Equal(verificationTicket, ReadInputValue(verificationHtml, "SecondFactorTicket"));
        Assert.Contains("name=\"SecondFactorCode\"", verificationHtml);
        var beforeCompletion = new Dictionary<string, string>(freshCookies, StringComparer.Ordinal);
        using var completed = await SendFormAsync(client, "/hello/login?handler=SecondFactorVerify", freshCookies,
            new Dictionary<string, string> { ["SecondFactorTicket"] = verificationTicket, ["SecondFactorCode"] = recoveryCode, ["mode"] = "verify", ["__RequestVerificationToken"] = ReadInputValue(verificationHtml, "__RequestVerificationToken") });
        MergeCookies(freshCookies, completed);
        Assert.Contains(freshCookies.Keys, name => name.Contains("refresh", StringComparison.OrdinalIgnoreCase));
        using var replayVerification = await SendFormAsync(client, "/hello/login?handler=SecondFactorVerify", beforeCompletion,
            new Dictionary<string, string> { ["SecondFactorTicket"] = verificationTicket, ["SecondFactorCode"] = recoveryCode, ["mode"] = "verify", ["__RequestVerificationToken"] = ReadInputValue(verificationHtml, "__RequestVerificationToken") });
        AssertNoSession(replayVerification);
    }
    private static async Task<string> BeginPhoneSignInAsync(HttpClient client, TestApplication app, Dictionary<string, string> cookies)
    {
        using var page = await SendAsync(client, HttpMethod.Get, "/hello/login", cookies);
        MergeCookies(cookies, page);
        var html = await page.Content.ReadAsStringAsync();
        var count = app.Messages.Count(message => message.Kind == HelloAccountMessageKind.PhoneSignInVerification);
        using var request = await SendFormAsync(client, "/hello/login?handler=PhoneRequest", cookies,
            new Dictionary<string, string> { ["Phone.PhoneNumber"] = "+79991234573", ["__RequestVerificationToken"] = ReadInputValue(html, "__RequestVerificationToken") });
        MergeCookies(cookies, request);
        html = await request.Content.ReadAsStringAsync();
        for (var i = 0; i < 100 && app.Messages.Count(message => message.Kind == HelloAccountMessageKind.PhoneSignInVerification) <= count; i++)
            await Task.Delay(20);
        var messages = app.Messages.Where(message => message.Kind == HelloAccountMessageKind.PhoneSignInVerification).ToArray();
        Assert.True(messages.Length > count);
        var code = messages[^1].VerificationCode;
        Assert.NotNull(code);
        using var verify = await SendFormAsync(client, "/hello/login?handler=PhoneVerify", cookies,
            new Dictionary<string, string> { ["Phone.RequestId"] = ReadInputValue(html, "Phone.RequestId"), ["Phone.Code"] = code, ["__RequestVerificationToken"] = ReadInputValue(html, "__RequestVerificationToken") });
        Assert.Equal(HttpStatusCode.Redirect, verify.StatusCode);
        MergeCookies(cookies, verify);
        using var secondFactor = await SendAsync(client, HttpMethod.Get, verify.Headers.Location!.ToString(), cookies);
        MergeCookies(cookies, secondFactor);
        return await secondFactor.Content.ReadAsStringAsync();
    }
}
