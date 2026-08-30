using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.Endpoints;
using Skopka.Hello.Oidc;
using Skopka.Hello.UI;
using Skopka.Identity.Ef.PostgreSql;
using Skopka.Identity.Verification;
using Testcontainers.PostgreSql;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Skopka.Hello.IntegrationTests;

public sealed class ExternalOidcFlowTests
{
    private const string ProviderId = "integration";
    private const string ClientId = "skopka-hello-integration";
    private const string ClientSecret =
        "test-client-secret-that-is-long-enough";
    private const string Subject = "external-subject-42";
    private const string UiCookieName = "__Host-Skopka.Hello.UI";
    private const string RefreshCookieName =
        "__Host-Skopka.Hello.Refresh";
    private const string AntiforgeryRequestCookieName =
        "__Host-Skopka.Hello.XSRF-TOKEN";
    private static readonly Uri PublicOrigin =
        new("https://hello.test/");
    private static readonly Uri AuthorityOrigin =
        new("https://oidc.test/");

    [Fact]
    public async Task AuthorizationCodeFlowRegistersAndReusesExternalIdentity()
    {
        await using var postgres = new PostgreSqlBuilder(
                "postgres:17-alpine")
            .Build();
        await postgres.StartAsync();

        await using var authority =
            await TestOidcAuthority.CreateAsync();
        await using var hello = await TestHelloApplication.CreateAsync(
            postgres.GetConnectionString(),
            authority);
        using var helloClient = hello.CreateClient();

        Dictionary<string, string> firstCookies =
            new(StringComparer.Ordinal);
        var first = await CompleteProviderChallengeAsync(
            helloClient,
            authority,
            firstCookies);

        Assert.Equal(
            HelloUiDefaults.ExternalRegistrationPath,
            first.CompletionLocation);
        AssertAuthorizationRequest(first.AuthorizationRequest);
        Assert.Equal(
            first.AuthorizationRequest.State,
            first.CallbackState);
        Assert.False(string.IsNullOrWhiteSpace(first.AuthorizationCode));

        using var registrationPage = await SendHelloAsync(
            helloClient,
            HttpMethod.Get,
            HelloUiDefaults.ExternalRegistrationPath,
            firstCookies);
        Assert.Equal(HttpStatusCode.OK, registrationPage.StatusCode);
        var registrationHtml =
            await registrationPage.Content.ReadAsStringAsync();
        Assert.Equal(
            "external@example.test",
            ReadInputValue(registrationHtml, "Input.Email"));
        Assert.DoesNotContain(
            "name=\"Input.Locale\"",
            registrationHtml,
            StringComparison.Ordinal);
        Assert.Contains(
            "name=\"Input.AcceptTermsOfService\"",
            registrationHtml,
            StringComparison.Ordinal);
        Assert.Contains(
            "name=\"Input.AcceptPrivacyPolicy\"",
            registrationHtml,
            StringComparison.Ordinal);
        var registrationToken = ReadInputValue(
            registrationHtml,
            "__RequestVerificationToken");

        using var missingLegalConsents = await SendHelloFormAsync(
            helloClient,
            HelloUiDefaults.ExternalRegistrationPath,
            firstCookies,
            new Dictionary<string, string>
            {
                ["Input.DisplayName"] = "External Alice",
                ["Input.Email"] = "external@example.test",
                ["Input.UserName"] = "external-alice",
                ["Input.Phone"] = string.Empty,
                ["Input.Locale"] = "en",
                ["__RequestVerificationToken"] = registrationToken,
            });
        Assert.Equal(HttpStatusCode.OK, missingLegalConsents.StatusCode);
        var missingLegalConsentsHtml =
            await missingLegalConsents.Content.ReadAsStringAsync();
        Assert.Contains(
            "Accept the Terms of Service to create an account.",
            missingLegalConsentsHtml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Accept the Privacy Policy to create an account.",
            missingLegalConsentsHtml,
            StringComparison.Ordinal);
        registrationToken = ReadInputValue(
            missingLegalConsentsHtml,
            "__RequestVerificationToken");

        using var registration = await SendHelloFormAsync(
            helloClient,
            HelloUiDefaults.ExternalRegistrationPath,
            firstCookies,
            new Dictionary<string, string>
            {
                ["Input.DisplayName"] = "External Alice",
                ["Input.Email"] = "external@example.test",
                ["Input.UserName"] = "external-alice",
                ["Input.Phone"] = string.Empty,
                ["Input.Locale"] = "en",
                ["Input.AcceptTermsOfService"] = "true",
                ["Input.AcceptPrivacyPolicy"] = "true",
                ["__RequestVerificationToken"] = registrationToken,
            });
        Assert.Equal(HttpStatusCode.Redirect, registration.StatusCode);
        Assert.Equal(
            HelloUiDefaults.AccountPath,
            registration.Headers.Location?.OriginalString);
        Assert.Contains(UiCookieName, firstCookies.Keys);
        Assert.Contains(RefreshCookieName, firstCookies.Keys);

        using var firstAccount = await SendHelloAsync(
            helloClient,
            HttpMethod.Get,
            HelloUiDefaults.AccountPath,
            firstCookies);
        Assert.Equal(HttpStatusCode.OK, firstAccount.StatusCode);
        Assert.Contains(
            "External Alice",
            await firstAccount.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        var afterRegistration = await hello.ReadIdentityStateAsync();
        Assert.Equal(1, afterRegistration.UserCount);
        Assert.Equal(1, afterRegistration.ExternalLoginCount);
        Assert.Equal(1, afterRegistration.SessionCount);
        Assert.Equal("INTEGRATION", afterRegistration.Provider);
        Assert.Equal(Subject, afterRegistration.Subject);

        using var confirmationRequest = await helloClient.PostAsJsonAsync(
            "/auth/email-confirmation/request",
            new { email = "external@example.test" });
        Assert.Equal(
            HttpStatusCode.Accepted,
            confirmationRequest.StatusCode);
        var confirmationMessage = await hello.WaitForMessageAsync(
            HelloAccountMessageKind.EmailConfirmation);
        var confirmationQuery = QueryHelpers.ParseQuery(
            Assert.IsType<Uri>(confirmationMessage.ActionUrl).Query);
        using var confirmation = await helloClient.PostAsJsonAsync(
            "/auth/email-confirmation/confirm",
            new
            {
                userId = Guid.Parse(
                    confirmationQuery["userId"].Single()!),
                email = confirmationQuery["email"].Single(),
                token = confirmationQuery["token"].Single(),
            });
        Assert.Equal(HttpStatusCode.NoContent, confirmation.StatusCode);

        Dictionary<string, string> repeatedCookies =
            new(StringComparer.Ordinal);
        var repeated = await CompleteProviderChallengeAsync(
            helloClient,
            authority,
            repeatedCookies);

        Assert.Equal(
            HelloUiDefaults.AccountPath,
            repeated.CompletionLocation);
        AssertAuthorizationRequest(repeated.AuthorizationRequest);
        Assert.Equal(
            repeated.AuthorizationRequest.State,
            repeated.CallbackState);
        Assert.False(string.IsNullOrWhiteSpace(repeated.AuthorizationCode));
        Assert.Contains(UiCookieName, repeatedCookies.Keys);
        Assert.Contains(RefreshCookieName, repeatedCookies.Keys);

        using var repeatedAccount = await SendHelloAsync(
            helloClient,
            HttpMethod.Get,
            HelloUiDefaults.AccountPath,
            repeatedCookies);
        Assert.Equal(HttpStatusCode.OK, repeatedAccount.StatusCode);

        var afterRepeatedLogin = await hello.ReadIdentityStateAsync();
        Assert.Equal(1, afterRepeatedLogin.UserCount);
        Assert.Equal(1, afterRepeatedLogin.ExternalLoginCount);
        Assert.Equal(2, afterRepeatedLogin.SessionCount);
        Assert.Equal("INTEGRATION", afterRepeatedLogin.Provider);
        Assert.Equal(Subject, afterRepeatedLogin.Subject);

        using var securityPage = await SendHelloAsync(
            helloClient,
            HttpMethod.Get,
            HelloUiDefaults.AccountSecurityPath,
            repeatedCookies);
        Assert.Equal(HttpStatusCode.OK, securityPage.StatusCode);
        var securityHtml = await securityPage.Content.ReadAsStringAsync();
        Assert.Contains(
            "No password is configured.",
            securityHtml,
            StringComparison.Ordinal);
        var securityToken = ReadInputValue(
            securityHtml,
            "__RequestVerificationToken");

        using var beginPasswordSet = await SendHelloFormAsync(
            helloClient,
            $"{HelloUiDefaults.AccountSecurityPath}?handler=BeginSet",
            repeatedCookies,
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = securityToken,
            });
        using var passwordSetPage = await FollowHelloRedirectAsync(
            helloClient,
            beginPasswordSet,
            repeatedCookies);
        Assert.Equal(HttpStatusCode.OK, passwordSetPage.StatusCode);
        var passwordSetHtml =
            await passwordSetPage.Content.ReadAsStringAsync();
        var challengeId = ReadInputValue(
            passwordSetHtml,
            "SetInput.ChallengeId");
        var completeToken = ReadInputValue(
            passwordSetHtml,
            "__RequestVerificationToken");
        var verificationMessage = await hello.WaitForMessageAsync(
            HelloAccountMessageKind.AccountSecurityVerification);
        var verificationCode = Assert.IsType<string>(
            verificationMessage.VerificationCode);
        const string newPassword =
            "external account password staple";

        using var completePasswordSet = await SendHelloFormAsync(
            helloClient,
            $"{HelloUiDefaults.AccountSecurityPath}?handler=CompleteSet",
            repeatedCookies,
            new Dictionary<string, string>
            {
                ["SetInput.ChallengeId"] = challengeId,
                ["SetInput.VerificationCode"] = verificationCode,
                ["SetInput.NewPassword"] = newPassword,
                ["SetInput.ConfirmPassword"] = newPassword,
                ["__RequestVerificationToken"] = completeToken,
            });
        Assert.Equal(HttpStatusCode.Redirect, completePasswordSet.StatusCode);
        Assert.StartsWith(
            HelloUiDefaults.LoginPath,
            completePasswordSet.Headers.Location?.OriginalString,
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, repeatedCookies[UiCookieName]);
        Assert.Equal(string.Empty, repeatedCookies[RefreshCookieName]);

        using var passwordLogin = await helloClient.PostAsJsonAsync(
            "/auth/login",
            new
            {
                login = "external@example.test",
                password = newPassword,
            });
        Assert.Equal(HttpStatusCode.OK, passwordLogin.StatusCode);

        Dictionary<string, string> removalCookies =
            new(StringComparer.Ordinal);
        var removalLogin = await CompleteProviderChallengeAsync(
            helloClient,
            authority,
            removalCookies);
        Assert.Equal(
            HelloUiDefaults.AccountPath,
            removalLogin.CompletionLocation);

        using var removalPage = await SendHelloAsync(
            helloClient,
            HttpMethod.Get,
            HelloUiDefaults.AccountSecurityPath,
            removalCookies);
        var removalPageHtml =
            await removalPage.Content.ReadAsStringAsync();
        Assert.Contains(
            "Request password removal",
            removalPageHtml,
            StringComparison.Ordinal);
        var removalToken = ReadInputValue(
            removalPageHtml,
            "__RequestVerificationToken");
        using var beginRemoval = await SendHelloFormAsync(
            helloClient,
            $"{HelloUiDefaults.AccountSecurityPath}?handler=BeginRemove",
            removalCookies,
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = removalToken,
            });
        using var removalChallengePage = await FollowHelloRedirectAsync(
            helloClient,
            beginRemoval,
            removalCookies);
        Assert.Equal(HttpStatusCode.OK, removalChallengePage.StatusCode);
        var removalHtml =
            await removalChallengePage.Content.ReadAsStringAsync();
        var removalChallengeId = ReadInputValue(
            removalHtml,
            "ActionInput.ChallengeId");
        var completeRemovalToken = ReadInputValue(
            removalHtml,
            "__RequestVerificationToken");
        var removalMessage = await hello.WaitForMessageAsync(
            HelloAccountMessageKind.AccountSecurityVerification,
            occurrence: 2);
        var removalCode = Assert.IsType<string>(
            removalMessage.VerificationCode);

        using var completeRemoval = await SendHelloFormAsync(
            helloClient,
            $"{HelloUiDefaults.AccountSecurityPath}?handler=CompleteRemove",
            removalCookies,
            new Dictionary<string, string>
            {
                ["ActionInput.ChallengeId"] = removalChallengeId,
                ["ActionInput.VerificationCode"] = removalCode,
                ["__RequestVerificationToken"] = completeRemovalToken,
            });
        Assert.Equal(HttpStatusCode.Redirect, completeRemoval.StatusCode);
        Assert.StartsWith(
            HelloUiDefaults.LoginPath,
            completeRemoval.Headers.Location?.OriginalString,
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, removalCookies[UiCookieName]);
        Assert.Equal(string.Empty, removalCookies[RefreshCookieName]);

        using var removedPasswordLogin =
            await helloClient.PostAsJsonAsync(
                "/auth/login",
                new
                {
                    login = "external@example.test",
                    password = newPassword,
                });
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            removedPasswordLogin.StatusCode);
    }

    [Fact]
    public async Task HeadlessBrowserFlowRegistersAndReusesExternalIdentity()
    {
        await using var postgres = new PostgreSqlBuilder(
                "postgres:17-alpine")
            .Build();
        await postgres.StartAsync();

        await using var authority =
            await TestOidcAuthority.CreateAsync();
        await using var hello = await TestHelloApplication.CreateAsync(
            postgres.GetConnectionString(),
            authority);
        using var helloClient = hello.CreateClient();

        const string returnUrl = "/app/auth-callback";
        Dictionary<string, string> firstCookies =
            new(StringComparer.Ordinal);
        var first = await CompleteHeadlessProviderCallbackAsync(
            helloClient,
            authority,
            firstCookies,
            returnUrl);

        AssertAuthorizationRequest(first.AuthorizationRequest);
        Assert.Equal(
            first.AuthorizationRequest.State,
            first.CallbackState);
        Assert.False(string.IsNullOrWhiteSpace(first.AuthorizationCode));
        Assert.True(firstCookies.TryGetValue(
            AntiforgeryRequestCookieName,
            out var antiforgeryToken));

        using (var missingAntiforgery = await SendHelloJsonAsync(
                   helloClient,
                   HttpMethod.Post,
                   HelloOidcDefaults.ApiCompletionPath,
                   firstCookies))
        {
            Assert.Equal(
                HttpStatusCode.Forbidden,
                missingAntiforgery.StatusCode);
        }

        using var completion = await SendHelloJsonAsync(
            helloClient,
            HttpMethod.Post,
            HelloOidcDefaults.ApiCompletionPath,
            firstCookies,
            antiforgeryToken: antiforgeryToken);
        Assert.Equal(HttpStatusCode.OK, completion.StatusCode);
        var completionJson = await completion.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Subject, completionJson, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "providerToken",
            completionJson,
            StringComparison.OrdinalIgnoreCase);
        var registrationRequired =
            await completion.Content.ReadFromJsonAsync<
                ExternalAuthenticationResponse>();
        Assert.NotNull(registrationRequired);
        Assert.Equal(
            ExternalAuthenticationOutcome.RegistrationRequired,
            registrationRequired.Outcome);
        Assert.Null(registrationRequired.Session);
        Assert.Equal(returnUrl, registrationRequired.ReturnUrl);
        Assert.Equal(
            ProviderId,
            registrationRequired.Registration?.Provider.ProviderId);
        Assert.Equal(
            "Integration authority",
            registrationRequired.Registration?.Provider.DisplayName);
        Assert.Equal(
            "Provider Alice",
            registrationRequired.Registration?.DisplayName);
        Assert.Equal(
            "external@example.test",
            registrationRequired.Registration?.VerifiedEmail);
        Assert.Equal("en", registrationRequired.Registration?.Locale);

        using var registrationHints = await SendHelloJsonAsync(
            helloClient,
            HttpMethod.Get,
            HelloOidcDefaults.ApiRegistrationPath,
            firstCookies);
        Assert.Equal(HttpStatusCode.OK, registrationHints.StatusCode);
        var hints = await registrationHints.Content.ReadFromJsonAsync<
            ExternalAuthenticationResponse>();
        Assert.Equal(
            ExternalAuthenticationOutcome.RegistrationRequired,
            Assert.IsType<ExternalAuthenticationResponse>(hints).Outcome);

        using var missingConsentRegistration = await SendHelloJsonAsync(
            helloClient,
            HttpMethod.Post,
            HelloOidcDefaults.ApiRegistrationPath,
            firstCookies,
            new
            {
                userName = "external-headless-alice",
                email = "external@example.test",
                phone = (string?)null,
                profile = new
                {
                    displayName = "Headless Alice",
                    locale = "en",
                },
            },
            antiforgeryToken);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            missingConsentRegistration.StatusCode);

        using var registration = await SendHelloJsonAsync(
            helloClient,
            HttpMethod.Post,
            HelloOidcDefaults.ApiRegistrationPath,
            firstCookies,
            new
            {
                userName = "external-headless-alice",
                email = "external@example.test",
                phone = (string?)null,
                profile = new
                {
                    displayName = "Headless Alice",
                    locale = "en",
                },
                acceptTermsOfService = true,
                acceptPrivacyPolicy = true,
            },
            antiforgeryToken);
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        var signedIn = await registration.Content.ReadFromJsonAsync<
            ExternalAuthenticationResponse>();
        Assert.NotNull(signedIn);
        Assert.Equal(
            ExternalAuthenticationOutcome.SignedIn,
            signedIn.Outcome);
        Assert.NotNull(signedIn.Session);
        Assert.False(string.IsNullOrWhiteSpace(
            signedIn.Session.AccessToken));
        Assert.Null(signedIn.Registration);
        Assert.Equal(returnUrl, signedIn.ReturnUrl);
        Assert.Contains(RefreshCookieName, firstCookies.Keys);

        antiforgeryToken = firstCookies[AntiforgeryRequestCookieName];
        using var replay = await SendHelloJsonAsync(
            helloClient,
            HttpMethod.Post,
            HelloOidcDefaults.ApiRegistrationPath,
            firstCookies,
            new
            {
                userName = "replayed-registration",
                email = "external@example.test",
                phone = (string?)null,
                profile = new
                {
                    displayName = "Replay",
                    locale = "en",
                },
                acceptTermsOfService = true,
                acceptPrivacyPolicy = true,
            },
            antiforgeryToken);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        var afterRegistration = await hello.ReadIdentityStateAsync();
        Assert.Equal(1, afterRegistration.UserCount);
        Assert.Equal(1, afterRegistration.ExternalLoginCount);
        Assert.Equal(1, afterRegistration.SessionCount);
        Assert.Equal("INTEGRATION", afterRegistration.Provider);
        Assert.Equal(Subject, afterRegistration.Subject);

        Dictionary<string, string> repeatedCookies =
            new(StringComparer.Ordinal);
        var repeated = await CompleteHeadlessProviderCallbackAsync(
            helloClient,
            authority,
            repeatedCookies,
            returnUrl);
        AssertAuthorizationRequest(repeated.AuthorizationRequest);
        antiforgeryToken =
            repeatedCookies[AntiforgeryRequestCookieName];

        using var repeatedCompletion = await SendHelloJsonAsync(
            helloClient,
            HttpMethod.Post,
            HelloOidcDefaults.ApiCompletionPath,
            repeatedCookies,
            antiforgeryToken: antiforgeryToken);
        Assert.Equal(HttpStatusCode.OK, repeatedCompletion.StatusCode);
        var repeatedSignIn =
            await repeatedCompletion.Content.ReadFromJsonAsync<
                ExternalAuthenticationResponse>();
        Assert.NotNull(repeatedSignIn);
        Assert.Equal(
            ExternalAuthenticationOutcome.SignedIn,
            repeatedSignIn.Outcome);
        Assert.NotNull(repeatedSignIn.Session);
        Assert.Null(repeatedSignIn.Registration);
        Assert.Contains(RefreshCookieName, repeatedCookies.Keys);

        var afterRepeatedLogin = await hello.ReadIdentityStateAsync();
        Assert.Equal(1, afterRepeatedLogin.UserCount);
        Assert.Equal(1, afterRepeatedLogin.ExternalLoginCount);
        Assert.Equal(2, afterRepeatedLogin.SessionCount);
    }

    [Fact]
    public async Task HeadlessBrowserFlowLinksAndUnlinksWithOtpStepUp()
    {
        await using var postgres = new PostgreSqlBuilder(
                "postgres:17-alpine")
            .Build();
        await postgres.StartAsync();

        await using var authority =
            await TestOidcAuthority.CreateAsync();
        await using var hello = await TestHelloApplication.CreateAsync(
            postgres.GetConnectionString(),
            authority);
        using var helloClient = hello.CreateClient();

        const string returnUrl = "/app/external-result";
        const string password = "Strong-Headless-Password-42!";
        Dictionary<string, string> cookies =
            new(StringComparer.Ordinal);
        await CompleteHeadlessProviderCallbackAsync(
            helloClient,
            authority,
            cookies,
            returnUrl);
        var antiforgeryToken =
            cookies[AntiforgeryRequestCookieName];

        using var completion = await SendHelloJsonAsync(
            helloClient,
            HttpMethod.Post,
            HelloOidcDefaults.ApiCompletionPath,
            cookies,
            antiforgeryToken: antiforgeryToken);
        Assert.Equal(HttpStatusCode.OK, completion.StatusCode);

        using var registration = await SendHelloJsonAsync(
            helloClient,
            HttpMethod.Post,
            HelloOidcDefaults.ApiRegistrationPath,
            cookies,
            new
            {
                userName = "headless-mutations",
                email = "external@example.test",
                phone = (string?)null,
                profile = new
                {
                    displayName = "Headless Mutations",
                    locale = "en",
                },
                acceptTermsOfService = true,
                acceptPrivacyPolicy = true,
            },
            antiforgeryToken);
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        var registrationResult =
            await registration.Content.ReadFromJsonAsync<
                ExternalAuthenticationResponse>();
        var accessToken = Assert.IsType<SessionResponse>(
            Assert.IsType<ExternalAuthenticationResponse>(
                registrationResult).Session).AccessToken;

        using var confirmationRequest =
            await helloClient.PostAsJsonAsync(
                "/auth/email-confirmation/request",
                new { email = "external@example.test" });
        Assert.Equal(
            HttpStatusCode.Accepted,
            confirmationRequest.StatusCode);
        var confirmationMessage = await hello.WaitForMessageAsync(
            HelloAccountMessageKind.EmailConfirmation);
        var confirmationQuery = QueryHelpers.ParseQuery(
            Assert.IsType<Uri>(confirmationMessage.ActionUrl).Query);
        using var confirmation = await helloClient.PostAsJsonAsync(
            "/auth/email-confirmation/confirm",
            new
            {
                userId = Guid.Parse(
                    confirmationQuery["userId"].Single()!),
                email = confirmationQuery["email"].Single(),
                token = confirmationQuery["token"].Single(),
            });
        Assert.Equal(HttpStatusCode.NoContent, confirmation.StatusCode);

        using var beginPasswordSet = await SendHelloJsonAsync(
            helloClient,
            HttpMethod.Post,
            "/account/password/set/challenge",
            cookies,
            accessToken: accessToken);
        Assert.Equal(HttpStatusCode.OK, beginPasswordSet.StatusCode);
        var passwordChallenge =
            await beginPasswordSet.Content.ReadFromJsonAsync<
                StepUpChallengeResponse>();
        var passwordMessage = await hello.WaitForMessageAsync(
            HelloAccountMessageKind.AccountSecurityVerification);
        using var completePasswordSet = await SendHelloJsonAsync(
            helloClient,
            HttpMethod.Put,
            "/account/password",
            cookies,
            new
            {
                challengeId = Assert.IsType<StepUpChallengeResponse>(
                    passwordChallenge).ChallengeId,
                verificationCode = Assert.IsType<string>(
                    passwordMessage.VerificationCode),
                newPassword = password,
            },
            accessToken: accessToken);
        Assert.Equal(
            HttpStatusCode.NoContent,
            completePasswordSet.StatusCode);

        using var passwordLogin = await SendHelloJsonAsync(
            helloClient,
            HttpMethod.Post,
            "/auth/login",
            cookies,
            new
            {
                login = "headless-mutations",
                password,
            });
        Assert.Equal(HttpStatusCode.OK, passwordLogin.StatusCode);
        var passwordSession =
            await passwordLogin.Content.ReadFromJsonAsync<SessionResponse>();
        accessToken = Assert.IsType<SessionResponse>(
            passwordSession).AccessToken;
        using var authenticatedAntiforgery = await SendHelloJsonAsync(
            helloClient,
            HttpMethod.Get,
            "/auth/antiforgery",
            cookies,
            accessToken: accessToken);
        Assert.Equal(
            HttpStatusCode.NoContent,
            authenticatedAntiforgery.StatusCode);
        antiforgeryToken = cookies[AntiforgeryRequestCookieName];
        Assert.True(
            antiforgeryToken.Length > 0,
            "Password login did not issue a readable antiforgery token.");
        Assert.True(
            cookies.TryGetValue(
                "__Host-Skopka.Hello.Antiforgery",
                out var antiforgeryCookie)
                && antiforgeryCookie.Length > 0,
            "The authenticated antiforgery endpoint did not issue its cookie.");

        using var beginUnlink = await SendHelloJsonAsync(
            helloClient,
            HttpMethod.Post,
            $"/account/external-logins/{ProviderId}/unlink/challenge",
            cookies,
            antiforgeryToken: antiforgeryToken,
            accessToken: accessToken);
        Assert.True(
            beginUnlink.StatusCode == HttpStatusCode.OK,
            $"Unexpected unlink challenge response: "
                + $"{beginUnlink.StatusCode} "
                + await beginUnlink.Content.ReadAsStringAsync());
        var unlinkMessage = await hello.WaitForMessageAsync(
            HelloAccountMessageKind.ExternalLoginUnlinkVerification);
        using var unlink = await SendHelloJsonAsync(
            helloClient,
            HttpMethod.Delete,
            "/account/external-logins/unlink",
            cookies,
            new
            {
                verificationCode = Assert.IsType<string>(
                    unlinkMessage.VerificationCode),
            },
            antiforgeryToken,
            accessToken);
        Assert.Equal(HttpStatusCode.OK, unlink.StatusCode);
        var unlinkSession =
            await unlink.Content.ReadFromJsonAsync<SessionResponse>();
        accessToken = Assert.IsType<SessionResponse>(
            unlinkSession).AccessToken;
        antiforgeryToken = cookies[AntiforgeryRequestCookieName];

        using var afterUnlink = await SendHelloJsonAsync(
            helloClient,
            HttpMethod.Get,
            "/account/external-logins",
            cookies,
            accessToken: accessToken);
        Assert.Equal(HttpStatusCode.OK, afterUnlink.StatusCode);
        Assert.Empty(Assert.IsType<LinkedExternalProviderResponse[]>(
            await afterUnlink.Content.ReadFromJsonAsync<
                LinkedExternalProviderResponse[]>()));

        using var prepareLink = await SendHelloJsonAsync(
            helloClient,
            HttpMethod.Post,
            $"/account/external-logins/{ProviderId}/link",
            cookies,
            new { returnUrl },
            antiforgeryToken,
            accessToken);
        Assert.Equal(HttpStatusCode.OK, prepareLink.StatusCode);
        var linkStart = await prepareLink.Content.ReadFromJsonAsync<
            ExternalLinkStartResponse>();
        var challengeUrl = Assert.IsType<ExternalLinkStartResponse>(
            linkStart).ChallengeUrl;
        var copiedPreflightCookies = new Dictionary<string, string>(
            cookies,
            StringComparer.Ordinal);

        var linkProvider =
            await CompleteHeadlessProviderCallbackFromPathAsync(
                helloClient,
                authority,
                cookies,
                challengeUrl,
                returnUrl);
        AssertAuthorizationRequest(linkProvider.AuthorizationRequest);

        using var replayedPreflight = await SendHelloAsync(
            helloClient,
            HttpMethod.Get,
            challengeUrl,
            copiedPreflightCookies);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            replayedPreflight.StatusCode);

        using var completeProviderLink = await SendHelloJsonAsync(
            helloClient,
            HttpMethod.Post,
            HelloOidcDefaults.ApiCompletionPath,
            cookies,
            antiforgeryToken: antiforgeryToken,
            accessToken: accessToken);
        Assert.Equal(HttpStatusCode.OK, completeProviderLink.StatusCode);
        var linkPending =
            await completeProviderLink.Content.ReadFromJsonAsync<
                ExternalAuthenticationResponse>();
        Assert.NotNull(linkPending);
        Assert.Equal(
            ExternalAuthenticationOutcome.LinkVerificationRequired,
            linkPending.Outcome);
        Assert.Equal(ProviderId, linkPending.Provider?.ProviderId);
        Assert.Null(linkPending.Session);
        Assert.Null(linkPending.Registration);

        using var beginLinkVerification = await SendHelloJsonAsync(
            helloClient,
            HttpMethod.Post,
            "/account/external-logins/link/challenge",
            cookies,
            antiforgeryToken: antiforgeryToken,
            accessToken: accessToken);
        Assert.Equal(
            HttpStatusCode.OK,
            beginLinkVerification.StatusCode);
        var linkMessage = await hello.WaitForMessageAsync(
            HelloAccountMessageKind.ExternalLoginLinkVerification);
        using var link = await SendHelloJsonAsync(
            helloClient,
            HttpMethod.Put,
            "/account/external-logins/link",
            cookies,
            new
            {
                verificationCode = Assert.IsType<string>(
                    linkMessage.VerificationCode),
            },
            antiforgeryToken,
            accessToken);
        Assert.Equal(HttpStatusCode.OK, link.StatusCode);
        var linkSession =
            await link.Content.ReadFromJsonAsync<SessionResponse>();
        accessToken = Assert.IsType<SessionResponse>(
            linkSession).AccessToken;

        using var afterLink = await SendHelloJsonAsync(
            helloClient,
            HttpMethod.Get,
            "/account/external-logins",
            cookies,
            accessToken: accessToken);
        Assert.Equal(HttpStatusCode.OK, afterLink.StatusCode);
        var linked = Assert.Single(
            Assert.IsType<LinkedExternalProviderResponse[]>(
                await afterLink.Content.ReadFromJsonAsync<
                    LinkedExternalProviderResponse[]>()));
        Assert.Equal(ProviderId, linked.ProviderId);
        Assert.True(linked.Enabled);
        Assert.True(linked.CanUnlink);
    }

    private static void AssertAuthorizationRequest(
        RecordedAuthorizationRequest request)
    {
        Assert.Equal("code", request.ResponseType);
        Assert.Equal("form_post", request.ResponseMode);
        Assert.Equal("S256", request.CodeChallengeMethod);
        Assert.False(string.IsNullOrWhiteSpace(request.CodeChallenge));
        Assert.False(string.IsNullOrWhiteSpace(request.Nonce));
        Assert.False(string.IsNullOrWhiteSpace(request.State));
        Assert.Equal(
            new Uri(
                PublicOrigin,
                $"{HelloOidcDefaults.CallbackPathPrefix.TrimStart('/')}"
                    + ProviderId),
            request.RedirectUri);
    }

    private static async Task<CompletedProviderChallenge>
        CompleteProviderChallengeAsync(
            HttpClient helloClient,
            TestOidcAuthority authority,
            Dictionary<string, string> cookies)
    {
        using var loginPage = await SendHelloAsync(
            helloClient,
            HttpMethod.Get,
            $"{HelloUiDefaults.LoginPath}?ReturnUrl=%2Fhello%2Faccount",
            cookies);
        Assert.Equal(HttpStatusCode.OK, loginPage.StatusCode);
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        var antiforgeryToken = ReadInputValue(
            loginHtml,
            "__RequestVerificationToken");

        using var challenge = await SendHelloFormAsync(
            helloClient,
            $"{HelloUiDefaults.LoginPath}?handler=External"
                + "&ReturnUrl=%2Fhello%2Faccount",
            cookies,
            new Dictionary<string, string>
            {
                ["providerId"] = ProviderId,
                ["ReturnUrl"] = HelloUiDefaults.AccountPath,
                ["__RequestVerificationToken"] = antiforgeryToken,
            });
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var authorizationLocation = Assert.IsType<Uri>(
            challenge.Headers.Location);
        Assert.Equal(AuthorityOrigin.Host, authorizationLocation.Host);

        var challengeQuery = QueryHelpers.ParseQuery(
            authorizationLocation.Query);
        Assert.Equal("code", ReadQuery(challengeQuery, "response_type"));
        Assert.Equal("form_post", ReadQuery(
            challengeQuery,
            "response_mode"));
        Assert.Equal("S256", ReadQuery(
            challengeQuery,
            "code_challenge_method"));
        Assert.False(string.IsNullOrWhiteSpace(
            ReadQuery(challengeQuery, "code_challenge")));
        Assert.False(string.IsNullOrWhiteSpace(
            ReadQuery(challengeQuery, "nonce")));
        Assert.False(string.IsNullOrWhiteSpace(
            ReadQuery(challengeQuery, "state")));

        using var authorization = await authority.Client.GetAsync(
            authorizationLocation.PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, authorization.StatusCode);
        var authorizationHtml =
            await authorization.Content.ReadAsStringAsync();
        var callbackLocation = new Uri(
            ReadFormAction(authorizationHtml),
            UriKind.Absolute);
        Assert.Equal(PublicOrigin.Host, callbackLocation.Host);
        Assert.Equal(
            HelloOidcDefaults.CallbackPathPrefix + ProviderId,
            callbackLocation.AbsolutePath);
        var callbackForm = ReadHiddenForm(authorizationHtml);
        var authorizationCode = callbackForm["code"];
        var callbackState = callbackForm["state"];

        using var callback = await SendHelloFormAsync(
            helloClient,
            callbackLocation.PathAndQuery,
            cookies,
            callbackForm);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal(
            HelloOidcDefaults.CompletionPath,
            callback.Headers.Location?.OriginalString);

        using var completionPage = await SendHelloAsync(
            helloClient,
            HttpMethod.Get,
            HelloOidcDefaults.CompletionPath,
            cookies);
        Assert.Equal(HttpStatusCode.OK, completionPage.StatusCode);
        var completionToken = ReadInputValue(
            await completionPage.Content.ReadAsStringAsync(),
            "__RequestVerificationToken");

        using var completion = await SendHelloFormAsync(
            helloClient,
            HelloOidcDefaults.CompletionPath,
            cookies,
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = completionToken,
            });
        Assert.Equal(HttpStatusCode.Redirect, completion.StatusCode);

        return new CompletedProviderChallenge(
            Assert.IsType<Uri>(completion.Headers.Location)
                .OriginalString,
            authority.DequeueAuthorizationRequest(),
            authorizationCode,
            callbackState);
    }

    private static async Task<CompletedHeadlessProviderChallenge>
        CompleteHeadlessProviderCallbackAsync(
            HttpClient helloClient,
            TestOidcAuthority authority,
            Dictionary<string, string> cookies,
            string returnUrl)
        => await CompleteHeadlessProviderCallbackFromPathAsync(
            helloClient,
            authority,
            cookies,
            $"{HelloOidcDefaults.ApiPathPrefix}{ProviderId}/challenge"
                + $"?returnUrl={Uri.EscapeDataString(returnUrl)}",
            returnUrl);

    private static async Task<CompletedHeadlessProviderChallenge>
        CompleteHeadlessProviderCallbackFromPathAsync(
            HttpClient helloClient,
            TestOidcAuthority authority,
            Dictionary<string, string> cookies,
            string challengePath,
            string returnUrl)
    {
        using var challenge = await SendHelloAsync(
            helloClient,
            HttpMethod.Get,
            challengePath,
            cookies);
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var authorizationLocation = Assert.IsType<Uri>(
            challenge.Headers.Location);
        Assert.Equal(AuthorityOrigin.Host, authorizationLocation.Host);

        var challengeQuery = QueryHelpers.ParseQuery(
            authorizationLocation.Query);
        Assert.Equal("code", ReadQuery(challengeQuery, "response_type"));
        Assert.Equal("form_post", ReadQuery(
            challengeQuery,
            "response_mode"));
        Assert.Equal("S256", ReadQuery(
            challengeQuery,
            "code_challenge_method"));

        using var authorization = await authority.Client.GetAsync(
            authorizationLocation.PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, authorization.StatusCode);
        var authorizationHtml =
            await authorization.Content.ReadAsStringAsync();
        var callbackLocation = new Uri(
            ReadFormAction(authorizationHtml),
            UriKind.Absolute);
        var callbackForm = ReadHiddenForm(authorizationHtml);

        using var callback = await SendHelloFormAsync(
            helloClient,
            callbackLocation.PathAndQuery,
            cookies,
            callbackForm);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal(
            returnUrl,
            callback.Headers.Location?.OriginalString);

        return new CompletedHeadlessProviderChallenge(
            authority.DequeueAuthorizationRequest(),
            callbackForm["code"],
            callbackForm["state"]);
    }

    private static async Task<HttpResponseMessage> SendHelloFormAsync(
        HttpClient client,
        string path,
        Dictionary<string, string> cookies,
        IReadOnlyDictionary<string, string> form)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(form),
        };
        AddCookies(request, cookies);
        var response = await client.SendAsync(request);
        MergeCookies(cookies, response);
        return response;
    }

    private static async Task<HttpResponseMessage> FollowHelloRedirectAsync(
        HttpClient client,
        HttpResponseMessage response,
        Dictionary<string, string> cookies)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        return await SendHelloAsync(
            client,
            HttpMethod.Get,
            location.OriginalString,
            cookies);
    }

    private static async Task<HttpResponseMessage> SendHelloAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        Dictionary<string, string> cookies)
    {
        using var request = new HttpRequestMessage(method, path);
        AddCookies(request, cookies);
        var response = await client.SendAsync(request);
        MergeCookies(cookies, response);
        return response;
    }

    private static async Task<HttpResponseMessage> SendHelloJsonAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        Dictionary<string, string> cookies,
        object? body = null,
        string? antiforgeryToken = null,
        string? accessToken = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (!string.IsNullOrWhiteSpace(antiforgeryToken))
        {
            request.Headers.TryAddWithoutValidation(
                "X-CSRF-TOKEN",
                antiforgeryToken);
        }

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.TryAddWithoutValidation(
                "Authorization",
                $"Bearer {accessToken}");
        }

        AddCookies(request, cookies);
        var response = await client.SendAsync(request);
        MergeCookies(cookies, response);
        return response;
    }

    private static void AddCookies(
        HttpRequestMessage request,
        Dictionary<string, string> cookies)
    {
        if (cookies.Count == 0)
        {
            return;
        }

        request.Headers.TryAddWithoutValidation(
            "Cookie",
            string.Join(
                "; ",
                cookies
                    .Where(pair => pair.Value.Length > 0)
                    .Select(pair => $"{pair.Key}={pair.Value}")));
    }

    private static void MergeCookies(
        Dictionary<string, string> cookies,
        HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return;
        }

        foreach (var value in values)
        {
            var pair = value.Split(';', 2)[0];
            var separator = pair.IndexOf('=');
            if (separator > 0)
            {
                cookies[pair[..separator]] = pair[(separator + 1)..];
            }
        }
    }

    private static string ReadInputValue(string html, string name)
    {
        var match = Regex.Match(
            html,
            $"<input[^>]*name=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);
        Assert.True(
            match.Success,
            $"Input '{name}' was not found in the rendered page.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static string ReadFormAction(string html)
    {
        var match = Regex.Match(
            html,
            "<form[^>]*action=[\"']([^\"']+)[\"']",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        Assert.True(match.Success, "The OIDC form_post action was not found.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static Dictionary<string, string> ReadHiddenForm(string html)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        foreach (Match input in Regex.Matches(
                     html,
                     "<input[^>]*>",
                     RegexOptions.CultureInvariant
                         | RegexOptions.IgnoreCase))
        {
            var name = ReadAttribute(input.Value, "name");
            var value = ReadAttribute(input.Value, "value");
            if (name is not null && value is not null)
            {
                values[name] = value;
            }
        }

        Assert.Contains("code", values.Keys);
        Assert.Contains("state", values.Keys);
        return values;
    }

    private static string? ReadAttribute(string element, string name)
    {
        var match = Regex.Match(
            element,
            $"{Regex.Escape(name)}=[\"']([^\"']*)[\"']",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        return match.Success
            ? WebUtility.HtmlDecode(match.Groups[1].Value)
            : null;
    }

    private static string ReadQuery(
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query,
        string name)
    {
        Assert.True(query.TryGetValue(name, out var values));
        return Assert.Single(values.ToArray())!;
    }

    private sealed record IntegrationProfile(
        string DisplayName,
        string? Locale)
    {
        public HelloRegistrationConsent? RegistrationConsent
        {
            get;
            init;
        }
    }

    private sealed class IntegrationProfileUiFactory
        : IHelloUiProfileFactory<IntegrationProfile>,
            IHelloRegistrationConsentProfileEnricher<IntegrationProfile>
    {
        public OperationResult<IntegrationProfile> Create(
            HelloUiRegistrationProfile profile)
            => OperationResultFactory.Success(
                new IntegrationProfile(
                    profile.DisplayName,
                    profile.Locale)
                {
                    RegistrationConsent =
                        profile.RegistrationConsent,
                });

        public string GetDisplayName(IntegrationProfile profile)
            => profile.DisplayName;

        public OperationResult<IntegrationProfile> Enrich(
            IntegrationProfile profile,
            HelloRegistrationConsent consent)
            => OperationResultFactory.Success(
                profile with
                {
                    RegistrationConsent = consent,
                });
    }

    private sealed record CompletedProviderChallenge(
        string CompletionLocation,
        RecordedAuthorizationRequest AuthorizationRequest,
        string AuthorizationCode,
        string CallbackState);

    private sealed record CompletedHeadlessProviderChallenge(
        RecordedAuthorizationRequest AuthorizationRequest,
        string AuthorizationCode,
        string CallbackState);

    private sealed record RecordedAuthorizationRequest(
        string? ResponseType,
        string? ResponseMode,
        string? CodeChallenge,
        string? CodeChallengeMethod,
        string? Nonce,
        string? State,
        Uri? RedirectUri);

    private sealed record IdentityState(
        int UserCount,
        int ExternalLoginCount,
        int SessionCount,
        string Provider,
        string Subject);

    private sealed class RecordingAccountMessageSender
        : IHelloAccountMessageSender
    {
        private readonly object sync = new();
        private readonly List<HelloAccountMessage> messages = [];

        public IReadOnlyList<HelloAccountMessage> Messages
        {
            get
            {
                lock (sync)
                {
                    return messages.ToArray();
                }
            }
        }

        public Task<OperationResult> SendAsync(
            HelloAccountMessage message,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (sync)
            {
                messages.Add(message);
            }

            return Task.FromResult(OperationResultFactory.Success());
        }
    }

    private sealed class AuthorityRequestRecorder
    {
        private readonly ConcurrentQueue<RecordedAuthorizationRequest>
            requests = new();

        public void Record(OpenIddictRequest request)
            => requests.Enqueue(
                new RecordedAuthorizationRequest(
                    request.ResponseType,
                    request.ResponseMode,
                    request.CodeChallenge,
                    request.CodeChallengeMethod,
                    request.Nonce,
                    request.State,
                    Uri.TryCreate(
                        request.RedirectUri,
                        UriKind.Absolute,
                        out var redirectUri)
                            ? redirectUri
                            : null));

        public RecordedAuthorizationRequest Dequeue()
            => requests.TryDequeue(out var request)
                ? request
                : throw new InvalidOperationException(
                    "The authority did not observe an authorization request.");
    }

    private sealed class TestOidcDbContext(
        DbContextOptions<TestOidcDbContext> options)
        : DbContext(options)
    {
    }

    private sealed class TestOidcAuthority : IAsyncDisposable
    {
        private readonly WebApplication application;
        private readonly SqliteConnection connection;
        private readonly AuthorityRequestRecorder recorder;

        private TestOidcAuthority(
            WebApplication application,
            SqliteConnection connection,
            AuthorityRequestRecorder recorder,
            HttpClient client)
        {
            this.application = application;
            this.connection = connection;
            this.recorder = recorder;
            Client = client;
        }

        public HttpClient Client { get; }

        public static async Task<TestOidcAuthority> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var recorder = new AuthorityRequestRecorder();

            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    EnvironmentName = "IntegrationTests",
                });
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(recorder);
            builder.Services.AddDbContext<TestOidcDbContext>(options =>
            {
                options.UseSqlite(connection);
                options.UseOpenIddict();
            });
            builder.Services.AddAuthorization();
            builder.Services.AddOpenIddict()
                .AddCore(options =>
                    options.UseEntityFrameworkCore()
                        .UseDbContext<TestOidcDbContext>())
                .AddServer(options =>
                {
                    options.SetIssuer(AuthorityOrigin);
                    options.SetAuthorizationEndpointUris(
                        "connect/authorize");
                    options.SetTokenEndpointUris("connect/token");
                    options.AllowAuthorizationCodeFlow();
                    options.RequireProofKeyForCodeExchange();
                    options.RegisterScopes(
                        Scopes.Email,
                        Scopes.Profile);
                    options.AddEphemeralEncryptionKey();
                    options.AddEphemeralSigningKey();
                    options.UseAspNetCore()
                        .EnableAuthorizationEndpointPassthrough();
                });

            var application = builder.Build();
            application.UseAuthentication();
            application.UseAuthorization();
            application.MapMethods(
                "/connect/authorize",
                [HttpMethods.Get, HttpMethods.Post],
                (HttpContext context, AuthorityRequestRecorder capture) =>
                {
                    var request = context.GetOpenIddictServerRequest()
                        ?? throw new InvalidOperationException(
                            "OpenIddict did not expose the authorization request.");
                    capture.Record(request);

                    var identity = new ClaimsIdentity(
                        OpenIddictServerAspNetCoreDefaults
                            .AuthenticationScheme);
                    identity.AddClaim(new Claim(Claims.Subject, Subject));
                    identity.AddClaim(
                        new Claim(Claims.Name, "Provider Alice")
                            .SetDestinations(Destinations.IdentityToken));
                    identity.AddClaim(
                        new Claim(
                            Claims.Email,
                            "external@example.test")
                            .SetDestinations(Destinations.IdentityToken));
                    identity.AddClaim(
                        new Claim(
                            Claims.EmailVerified,
                            "true",
                            ClaimValueTypes.Boolean)
                            .SetDestinations(Destinations.IdentityToken));
                    identity.AddClaim(
                        new Claim(Claims.Locale, "en")
                            .SetDestinations(Destinations.IdentityToken));

                    var principal = new ClaimsPrincipal(identity);
                    principal.SetScopes(request.GetScopes());
                    return Results.SignIn(
                        principal,
                        authenticationScheme:
                            OpenIddictServerAspNetCoreDefaults
                                .AuthenticationScheme);
                });

            await using (var scope =
                application.Services.CreateAsyncScope())
            {
                var database = scope.ServiceProvider
                    .GetRequiredService<TestOidcDbContext>();
                await database.Database.EnsureCreatedAsync();

                var manager = scope.ServiceProvider
                    .GetRequiredService<IOpenIddictApplicationManager>();
                var descriptor = new OpenIddictApplicationDescriptor
                {
                    ClientId = ClientId,
                    ClientSecret = ClientSecret,
                    ClientType = ClientTypes.Confidential,
                    ConsentType = ConsentTypes.Implicit,
                    DisplayName = "Skopka.Hello integration client",
                };
                descriptor.RedirectUris.Add(
                    new Uri(
                        PublicOrigin,
                        $"{HelloOidcDefaults.CallbackPathPrefix.TrimStart('/')}"
                            + ProviderId));
                descriptor.Permissions.UnionWith(
                    [
                        Permissions.Endpoints.Authorization,
                        Permissions.Endpoints.Token,
                        Permissions.GrantTypes.AuthorizationCode,
                        Permissions.ResponseTypes.Code,
                        Permissions.Scopes.Email,
                        Permissions.Scopes.Profile,
                    ]);
                descriptor.Requirements.Add(
                    Requirements.Features.ProofKeyForCodeExchange);
                await manager.CreateAsync(descriptor);
            }

            await application.StartAsync();
            var client = application.GetTestClient();
            client.BaseAddress = AuthorityOrigin;
            return new TestOidcAuthority(
                application,
                connection,
                recorder,
                client);
        }

        public HttpMessageHandler CreateBackchannelHandler()
            => application.GetTestServer().CreateHandler();

        public RecordedAuthorizationRequest DequeueAuthorizationRequest()
            => recorder.Dequeue();

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await application.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class TestHelloApplication : IAsyncDisposable
    {
        private readonly WebApplication application;
        private readonly RecordingAccountMessageSender messageSender;

        private TestHelloApplication(
            WebApplication application,
            RecordingAccountMessageSender messageSender)
        {
            this.application = application;
            this.messageSender = messageSender;
        }

        public async Task<HelloAccountMessage> WaitForMessageAsync(
            HelloAccountMessageKind kind,
            int occurrence = 1)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(
                occurrence,
                1);
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(10));
            while (!timeout.IsCancellationRequested)
            {
                var matches = messageSender.Messages
                    .Where(message => message.Kind == kind)
                    .ToArray();
                if (matches.Length >= occurrence)
                {
                    return matches[occurrence - 1];
                }

                try
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(10),
                        timeout.Token);
                }
                catch (OperationCanceledException)
                    when (timeout.IsCancellationRequested)
                {
                    break;
                }
            }

            throw new TimeoutException(
                $"No '{kind}' account message was recorded.");
        }

        public static async Task<TestHelloApplication> CreateAsync(
            string connectionString,
            TestOidcAuthority authority)
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    EnvironmentName = "IntegrationTests",
                });
            builder.WebHost.ConfigureKestrel(options =>
                options.Listen(IPAddress.Loopback, 0));
            var messageSender = new RecordingAccountMessageSender();
            builder.Services.AddSingleton<IHelloAccountMessageSender>(
                messageSender);

            var identity = builder.Services
                .AddSkopkaHello<IntegrationProfile>(options =>
                {
                    options.PublicOrigin = PublicOrigin;
                })
                .UsePostgreSql(connectionString)
                .UsePbkdf2PasswordHasher(options =>
                {
                    options.Iterations = 1_000;
                    options.MaximumAcceptedIterations = 1_000;
                })
                .UseDataProtectionActionTokens()
                .UseJwtSessions(
                    RandomNumberGenerator.GetBytes(32),
                    options =>
                    {
                        options.Issuer = PublicOrigin.AbsoluteUri;
                        options.Audience =
                            "skopka-hello-oidc-integration";
                    });
            var verificationKeys =
                new Dictionary<string, byte[]>
                {
                    ["v1"] = RandomNumberGenerator.GetBytes(32),
                };
            try
            {
                var verificationKeyProvider =
                    new StaticVerificationCodeKeyProvider(
                        "v1",
                        verificationKeys);
                identity.UseHmacOneTimeCodes(
                    verificationKeyProvider);
                identity.Services.RemoveAll<
                    IVerificationCodeKeyProvider>();
                identity.Services.AddSingleton<
                    IVerificationCodeKeyProvider>(
                    _ => verificationKeyProvider);
            }
            finally
            {
                foreach (var key in verificationKeys.Values)
                {
                    CryptographicOperations.ZeroMemory(key);
                }
            }
            identity.UseJwtBearerAuthentication();

            builder.Services.AddSkopkaHelloOidc<IntegrationProfile>(
                options =>
                {
                    options.PublicOrigin = PublicOrigin;
                    options.Providers[ProviderId] =
                        new HelloOidcProviderOptions
                        {
                            DisplayName = "Integration authority",
                            Authority = AuthorityOrigin.AbsoluteUri,
                            ClientId = ClientId,
                            ClientSecret = ClientSecret,
                        };
                });
            builder.Services.Configure<OpenIdConnectOptions>(
                HelloOidcDefaults.ProviderSchemePrefix + ProviderId,
                options => options.BackchannelHttpHandler =
                    authority.CreateBackchannelHandler());
            builder.Services.AddProblemDetails();
            builder.Services.AddSkopkaHelloUi<
                IntegrationProfile,
                IntegrationProfileUiFactory>(options =>
                {
                    options.TermsOfServiceUrl = "/terms";
                    options.PrivacyPolicyUrl = "/privacy";
                });

            var application = builder.Build();
            application.UseSkopkaHelloUiErrorPages();
            application.Use(
                static (context, next) =>
                {
                    context.Request.Scheme = "https";
                    context.Request.Host = new HostString(
                        PublicOrigin.Host);
                    return next(context);
                });
            application.UseAuthentication();
            application.UseAuthorization();
            application.MapSkopkaHello<IntegrationProfile>();
            application.MapSkopkaHelloUi();

            await using (var scope =
                application.Services.CreateAsyncScope())
            {
                var database = scope.ServiceProvider
                    .GetRequiredService<
                        PostgreSqlIdentityDbContext<IntegrationProfile>>();
                await database.Database.MigrateAsync();
            }

            await application.StartAsync();
            return new TestHelloApplication(
                application,
                messageSender);
        }

        public HttpClient CreateClient()
        {
            var server = application.Services
                .GetRequiredService<IServer>();
            var address = server.Features
                .Get<IServerAddressesFeature>()
                ?.Addresses
                .Single()
                ?? throw new InvalidOperationException(
                    "Kestrel did not expose its address.");
            var handler = new HttpClientHandler
            {
                UseProxy = false,
                UseCookies = false,
                AllowAutoRedirect = false,
            };
            return new HttpClient(handler)
            {
                BaseAddress = new Uri(address),
                Timeout = TimeSpan.FromSeconds(30),
            };
        }

        public async Task<IdentityState> ReadIdentityStateAsync()
        {
            await using var scope =
                application.Services.CreateAsyncScope();
            var database = scope.ServiceProvider
                .GetRequiredService<
                    PostgreSqlIdentityDbContext<IntegrationProfile>>();
            var login = await database.ExternalLogins
                .AsNoTracking()
                .SingleAsync();
            return new IdentityState(
                await database.Users.CountAsync(),
                await database.ExternalLogins.CountAsync(),
                await database.RefreshSessions.CountAsync(),
                login.Provider,
                login.Subject);
        }

        public async ValueTask DisposeAsync()
        {
            await application.DisposeAsync();
        }
    }
}
