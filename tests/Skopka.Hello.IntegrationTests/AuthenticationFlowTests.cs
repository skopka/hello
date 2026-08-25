using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.Admin;
using Skopka.Hello.Endpoints;
using Skopka.Hello.UI;
using Skopka.Identity;
using Skopka.Identity.Authentication;
using Skopka.Identity.Ef.PostgreSql;
using Skopka.Identity.Errors;
using Skopka.Identity.Roles;
using Skopka.Identity.Roles.Commands;
using Skopka.Identity.Users.Commands;
using Skopka.Identity.Users.Queries;
using Skopka.Identity.Verification;
using Testcontainers.PostgreSql;

namespace Skopka.Hello.IntegrationTests;

public sealed class AuthenticationFlowTests
{
    private const string RefreshCookieName =
        "__Host-Skopka.Hello.Refresh";
    private const string AntiforgeryCookieName =
        "__Host-Skopka.Hello.Antiforgery";
    private const string AntiforgeryRequestCookieName =
        "__Host-Skopka.Hello.XSRF-TOKEN";
    private const string AntiforgeryHeaderName = "X-CSRF-TOKEN";
    private const string UiCookieName = "__Host-Skopka.Hello.UI";
    private const string UiAdministratorPolicy =
        "Integration.Ui.Administrator";
    private const string UiNoticeText =
        "Test stand: <data> may be \"deleted\".";

    [Fact]
    public async Task ExplicitUserNameLoginRejectsEmailPasswordLogin()
    {
        await using var postgres = new PostgreSqlBuilder(
                "postgres:17-alpine")
            .Build();
        await postgres.StartAsync();

        await using var app = await TestApplication.CreateAsync(
            postgres.GetConnectionString(),
            configureHello: options =>
                options.PasswordLoginHandle =
                    PasswordLoginHandle.UserName);
        using var client = app.CreateClient();
        const string password = "correct horse battery staple";

        using var registration = await client.PostAsJsonAsync(
            "/auth/register",
            new
            {
                userName = "login-alice",
                email = "login-alice@example.test",
                phone = (string?)null,
                profile = new
                {
                    displayName = "Login Alice",
                    locale = "en",
                },
                password,
            });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        using var emailLogin = await client.PostAsJsonAsync(
            "/auth/login",
            new
            {
                login = "login-alice@example.test",
                password,
            });
        Assert.Equal(HttpStatusCode.Unauthorized, emailLogin.StatusCode);

        _ = await LoginAsync(client, "login-alice", password);
    }

    [Fact]
    public async Task AdministratorCanQueryAndOtpBlockUser()
    {
        await using var postgres = new PostgreSqlBuilder(
                "postgres:17-alpine")
            .Build();
        await postgres.StartAsync();

        await using var app = await TestApplication.CreateAsync(
            postgres.GetConnectionString());
        using var client = app.CreateClient();
        const string adminEmail = "admin-alice@example.test";
        const string targetEmail = "admin-target@example.test";
        const string password = "correct horse battery staple";

        using var adminRegistration = await client.PostAsJsonAsync(
            "/auth/register",
            new
            {
                userName = "admin-alice",
                email = adminEmail,
                phone = (string?)null,
                profile = new
                {
                    displayName = "Admin Alice",
                    locale = "en",
                },
                password,
            });
        Assert.Equal(HttpStatusCode.Created, adminRegistration.StatusCode);
        var admin = await adminRegistration.Content.ReadFromJsonAsync<
            AccountResponse<IntegrationProfile>>();
        Assert.NotNull(admin);

        using var targetRegistration = await client.PostAsJsonAsync(
            "/auth/register",
            new
            {
                userName = "admin-target",
                email = targetEmail,
                phone = (string?)null,
                profile = new
                {
                    displayName = "Admin Target",
                    locale = "en",
                },
                password,
            });
        Assert.Equal(HttpStatusCode.Created, targetRegistration.StatusCode);
        var target = await targetRegistration.Content.ReadFromJsonAsync<
            AccountResponse<IntegrationProfile>>();
        Assert.NotNull(target);

        var targetLogin = await LoginAsync(client, targetEmail, password);
        var unprivilegedAdminLogin = await LoginAsync(
            client,
            adminEmail,
            password);
        using (var forbiddenQuery = new HttpRequestMessage(
            HttpMethod.Get,
            "/admin/users"))
        {
            forbiddenQuery.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    unprivilegedAdminLogin.AccessToken);
            using var forbidden = await client.SendAsync(forbiddenQuery);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using var confirmationRequest = await client.PostAsJsonAsync(
            "/auth/email-confirmation/request",
            new { email = adminEmail });
        Assert.Equal(HttpStatusCode.Accepted, confirmationRequest.StatusCode);
        var confirmationMessage = await app.WaitForMessageAsync(
            HelloAccountMessageKind.EmailConfirmation);
        var confirmationQuery = QueryHelpers.ParseQuery(
            Assert.IsType<Uri>(confirmationMessage.ActionUrl).Query);
        using var confirmation = await client.PostAsJsonAsync(
            "/auth/email-confirmation/confirm",
            new
            {
                userId = Guid.Parse(
                    confirmationQuery["userId"].Single()!),
                email = confirmationQuery["email"].Single(),
                token = confirmationQuery["token"].Single(),
            });
        Assert.Equal(HttpStatusCode.NoContent, confirmation.StatusCode);

        await app.GrantAdministratorAsync(admin.Id);
        var adminLogin = await LoginAsync(client, adminEmail, password);

        using var queryRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/admin/users?search=admin-target");
        queryRequest.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                adminLogin.AccessToken);
        using var queried = await client.SendAsync(queryRequest);
        Assert.Equal(HttpStatusCode.OK, queried.StatusCode);
        using var queryJson = JsonDocument.Parse(
            await queried.Content.ReadAsStringAsync());
        var queriedTarget = Assert.Single(
            queryJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(target.Id, queriedTarget.GetProperty("id").GetGuid());
        var profileField = Assert.Single(
            queriedTarget.GetProperty("profile").EnumerateArray());
        Assert.Equal(
            "Admin Target",
            profileField.GetProperty("value").GetString());

        using var begin = await SendAuthorizedJsonAsync(
            client,
            HttpMethod.Post,
            $"/admin/users/{target.Id:D}/actions/block/challenge",
            adminLogin.AccessToken,
            new
            {
                expectedVersion = target.Version,
                reason = "integration security review",
            });
        Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
        using var beginJson = JsonDocument.Parse(
            await begin.Content.ReadAsStringAsync());
        var challengeId = beginJson.RootElement
            .GetProperty("challengeId")
            .GetGuid();
        var verificationMessage = await app.WaitForMessageAsync(
            HelloAccountMessageKind.AdminActionVerification);
        Assert.Equal(adminEmail, verificationMessage.RecipientAddress);
        var verificationCode = Assert.IsType<string>(
            verificationMessage.VerificationCode);

        using var complete = await SendAuthorizedJsonAsync(
            client,
            HttpMethod.Post,
            $"/admin/users/{target.Id:D}/actions/block",
            adminLogin.AccessToken,
            new
            {
                challengeId,
                verificationCode,
                expectedVersion = target.Version,
                reason = "integration security review",
            });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        using var beginCreateRole = await SendAuthorizedJsonAsync(
            client,
            HttpMethod.Post,
            "/admin/roles/actions/create/challenge",
            adminLogin.AccessToken,
            new
            {
                name = "Support Operators",
                description = "Integration role",
            });
        Assert.Equal(HttpStatusCode.OK, beginCreateRole.StatusCode);
        using var beginCreateRoleJson = JsonDocument.Parse(
            await beginCreateRole.Content.ReadAsStringAsync());
        var createRoleChallengeId = beginCreateRoleJson.RootElement
            .GetProperty("challengeId")
            .GetGuid();
        var createRoleCode = Assert.IsType<string>(
            app.Messages.Last(message => message.Kind
                    == HelloAccountMessageKind.AdminActionVerification)
                .VerificationCode);

        using var createRole = await SendAuthorizedJsonAsync(
            client,
            HttpMethod.Post,
            "/admin/roles/actions/create",
            adminLogin.AccessToken,
            new
            {
                challengeId = createRoleChallengeId,
                verificationCode = createRoleCode,
                name = "Support Operators",
                description = "Integration role",
            });
        Assert.Equal(HttpStatusCode.OK, createRole.StatusCode);
        using var createRoleJson = JsonDocument.Parse(
            await createRole.Content.ReadAsStringAsync());
        var createdRoleId = createRoleJson.RootElement
            .GetProperty("role")
            .GetProperty("id")
            .GetGuid();

        using var roleQuery = new HttpRequestMessage(
            HttpMethod.Get,
            "/admin/roles?search=support");
        roleQuery.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                adminLogin.AccessToken);
        using var queriedRoles = await client.SendAsync(roleQuery);
        Assert.Equal(HttpStatusCode.OK, queriedRoles.StatusCode);
        using var queriedRolesJson = JsonDocument.Parse(
            await queriedRoles.Content.ReadAsStringAsync());
        var queriedRole = Assert.Single(
            queriedRolesJson.RootElement
                .GetProperty("items")
                .EnumerateArray());
        Assert.Equal(createdRoleId, queriedRole.GetProperty("id").GetGuid());

        using var beginAssignRole = await SendAuthorizedJsonAsync(
            client,
            HttpMethod.Post,
            "/admin/roles/actions/assign/challenge",
            adminLogin.AccessToken,
            new
            {
                roleId = createdRoleId,
                targetUserId = target.Id,
            });
        Assert.Equal(HttpStatusCode.OK, beginAssignRole.StatusCode);
        using var beginAssignRoleJson = JsonDocument.Parse(
            await beginAssignRole.Content.ReadAsStringAsync());
        var assignRoleChallengeId = beginAssignRoleJson.RootElement
            .GetProperty("challengeId")
            .GetGuid();
        var assignRoleCode = Assert.IsType<string>(
            app.Messages.Last(message => message.Kind
                    == HelloAccountMessageKind.AdminActionVerification)
                .VerificationCode);

        using var assignRole = await SendAuthorizedJsonAsync(
            client,
            HttpMethod.Post,
            "/admin/roles/actions/assign",
            adminLogin.AccessToken,
            new
            {
                challengeId = assignRoleChallengeId,
                verificationCode = assignRoleCode,
                roleId = createdRoleId,
                targetUserId = target.Id,
            });
        Assert.Equal(HttpStatusCode.OK, assignRole.StatusCode);

        using var userRolesRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/admin/users/{target.Id:D}/roles");
        userRolesRequest.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                adminLogin.AccessToken);
        using var userRoles = await client.SendAsync(userRolesRequest);
        Assert.Equal(HttpStatusCode.OK, userRoles.StatusCode);
        using var userRolesJson = JsonDocument.Parse(
            await userRoles.Content.ReadAsStringAsync());
        Assert.Contains(
            userRolesJson.RootElement.EnumerateArray(),
            role => role.GetProperty("id").GetGuid() == createdRoleId);

        using var targetMe = new HttpRequestMessage(
            HttpMethod.Get,
            "/account/me");
        targetMe.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                targetLogin.AccessToken);
        using var targetRejected = await client.SendAsync(targetMe);
        Assert.Equal(HttpStatusCode.Unauthorized, targetRejected.StatusCode);
    }

    [Fact]
    public async Task CompleteAuthenticationAndSessionFlow()
    {
        await using var postgres = new PostgreSqlBuilder(
                "postgres:17-alpine")
            .Build();
        await postgres.StartAsync();

        await using var app = await TestApplication.CreateAsync(
            postgres.GetConnectionString());
        using var client = app.CreateClient();

        using var registration = await client.PostAsJsonAsync(
            "/auth/register",
            new
            {
                userName = "alice",
                email = "alice@example.test",
                phone = (string?)null,
                profile = new
                {
                    displayName = "Alice",
                    locale = "en",
                },
                password = "correct horse battery staple",
            });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        var firstLogin = await LoginAsync(client);
        Assert.Contains(
            "HttpOnly",
            firstLogin.RefreshSetCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Secure",
            firstLogin.RefreshSetCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "SameSite=Strict",
            firstLogin.RefreshSetCookie,
            StringComparison.OrdinalIgnoreCase);

        using var meRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/account/me");
        meRequest.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                firstLogin.AccessToken);
        using var me = await client.SendAsync(meRequest);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Contains(
            "no-store",
            me.Headers.CacheControl?.ToString(),
            StringComparison.OrdinalIgnoreCase);

        using var sessionsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/account/sessions");
        sessionsRequest.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                firstLogin.AccessToken);
        using var sessions = await client.SendAsync(sessionsRequest);
        Assert.Equal(HttpStatusCode.OK, sessions.StatusCode);
        Assert.Contains(
            "no-store",
            sessions.Headers.CacheControl?.ToString(),
            StringComparison.OrdinalIgnoreCase);

        var refreshed = await RefreshAsync(
            client,
            firstLogin.Cookies);
        Assert.NotEqual(
            firstLogin.AccessToken,
            refreshed.AccessToken);

        using var revokeRequest = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/account/sessions/{refreshed.SessionId}");
        revokeRequest.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                refreshed.AccessToken);
        using var revoked = await client.SendAsync(revokeRequest);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        using var replayRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/account/sessions");
        replayRequest.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                refreshed.AccessToken);
        using var replayed = await client.SendAsync(replayRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, replayed.StatusCode);

        var secondLogin = await LoginAsync(client);
        using var logout = CreateCookieMutation(
            HttpMethod.Post,
            "/auth/logout",
            secondLogin.Cookies);
        using var loggedOut = await client.SendAsync(logout);
        Assert.Equal(HttpStatusCode.NoContent, loggedOut.StatusCode);

        var thirdLogin = await LoginAsync(client);
        using var logoutAll = new HttpRequestMessage(
            HttpMethod.Post,
            "/auth/logout-all");
        logoutAll.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                thirdLogin.AccessToken);
        using var allLoggedOut = await client.SendAsync(logoutAll);
        Assert.Equal(
            HttpStatusCode.NoContent,
            allLoggedOut.StatusCode);
    }

    [Fact]
    public async Task AccountSelfServiceMutationsAreGenericAndVersioned()
    {
        await using var postgres = new PostgreSqlBuilder(
                "postgres:17-alpine")
            .Build();
        await postgres.StartAsync();

        await using var app = await TestApplication.CreateAsync(
            postgres.GetConnectionString());
        using var client = app.CreateClient();
        const string password = "correct horse battery staple";
        using var registration = await client.PostAsJsonAsync(
            "/auth/register",
            new
            {
                userName = "self-service",
                email = "self-service@example.test",
                phone = (string?)null,
                profile = new
                {
                    displayName = "Self Service",
                    locale = "en",
                },
                password,
            });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        var login = await LoginAsync(
            client,
            "self-service@example.test",
            password);
        using var initial = await GetMeAsync(client, login.AccessToken);
        var initialVersion = initial.RootElement
            .GetProperty("version")
            .GetInt64();

        using var changedName = await SendAuthorizedJsonAsync(
            client,
            HttpMethod.Put,
            "/account/user-name",
            login.AccessToken,
            new
            {
                expectedVersion = initialVersion,
                userName = "renamed-self-service",
            });
        Assert.Equal(HttpStatusCode.OK, changedName.StatusCode);
        using var nameAccount = JsonDocument.Parse(
            await changedName.Content.ReadAsStringAsync());
        var nameVersion = nameAccount.RootElement
            .GetProperty("version")
            .GetInt64();
        Assert.Equal(
            "renamed-self-service",
            nameAccount.RootElement.GetProperty("userName").GetString());

        using var changedEmail = await SendAuthorizedJsonAsync(
            client,
            HttpMethod.Put,
            "/account/email",
            login.AccessToken,
            new
            {
                expectedVersion = nameVersion,
                email = "renamed@example.test",
            });
        Assert.Equal(HttpStatusCode.OK, changedEmail.StatusCode);
        using var emailAccount = JsonDocument.Parse(
            await changedEmail.Content.ReadAsStringAsync());
        var emailVersion = emailAccount.RootElement
            .GetProperty("version")
            .GetInt64();
        Assert.False(
            emailAccount.RootElement
                .GetProperty("emailConfirmed")
                .GetBoolean());

        using var changedPhone = await SendAuthorizedJsonAsync(
            client,
            HttpMethod.Put,
            "/account/phone",
            login.AccessToken,
            new
            {
                expectedVersion = emailVersion,
                phone = "+1 555 010 4242",
            });
        Assert.Equal(HttpStatusCode.OK, changedPhone.StatusCode);
        using var phoneAccount = JsonDocument.Parse(
            await changedPhone.Content.ReadAsStringAsync());
        var phoneVersion = phoneAccount.RootElement
            .GetProperty("version")
            .GetInt64();
        Assert.False(
            phoneAccount.RootElement
                .GetProperty("phoneConfirmed")
                .GetBoolean());

        using var changedProfile = await SendAuthorizedJsonAsync(
            client,
            HttpMethod.Put,
            "/account/profile",
            login.AccessToken,
            new
            {
                expectedVersion = phoneVersion,
                profile = new
                {
                    displayName = "Updated profile",
                    locale = "ru",
                },
            });
        Assert.Equal(HttpStatusCode.OK, changedProfile.StatusCode);
        using var profileAccount = JsonDocument.Parse(
            await changedProfile.Content.ReadAsStringAsync());
        Assert.Equal(
            "Updated profile",
            profileAccount.RootElement
                .GetProperty("profile")
                .GetProperty("displayName")
                .GetString());

        using var staleMutation = await SendAuthorizedJsonAsync(
            client,
            HttpMethod.Put,
            "/account/user-name",
            login.AccessToken,
            new
            {
                expectedVersion = initialVersion,
                userName = "stale-write",
            });
        Assert.Equal(HttpStatusCode.Conflict, staleMutation.StatusCode);

        using var missingHandle = await client.PostAsJsonAsync(
            "/auth/register",
            new
            {
                userName = (string?)null,
                email = (string?)null,
                phone = (string?)null,
                profile = new
                {
                    displayName = "No Handle",
                    locale = "en",
                },
                password,
            });
        Assert.Equal(HttpStatusCode.BadRequest, missingHandle.StatusCode);

        const string phone = "+1 555 010 9898";
        using var phoneRegistration = await client.PostAsJsonAsync(
            "/auth/register",
            new
            {
                userName = (string?)null,
                email = (string?)null,
                phone,
                profile = new
                {
                    displayName = "Phone Only",
                    locale = "en",
                },
                password,
            });
        Assert.Equal(HttpStatusCode.Created, phoneRegistration.StatusCode);
        var phoneOnlyAccount = await phoneRegistration.Content
            .ReadFromJsonAsync<AccountResponse<IntegrationProfile>>();
        Assert.NotNull(phoneOnlyAccount);
        var phoneLogin = await LoginAsync(client, phone, password);

        using var removeLastHandle = await SendAuthorizedJsonAsync(
            client,
            HttpMethod.Put,
            "/account/phone",
            phoneLogin.AccessToken,
            new
            {
                expectedVersion = phoneOnlyAccount.Version,
                phone = (string?)null,
            });
        Assert.Equal(
            HttpStatusCode.Conflict,
            removeLastHandle.StatusCode);
        using var lastHandleProblem = JsonDocument.Parse(
            await removeLastHandle.Content.ReadAsStringAsync());
        Assert.Equal(
            HelloAccountSecurityActionErrorCodes.LastSignInMethod,
            lastHandleProblem.RootElement
                .GetProperty("code")
                .GetString());
    }

    [Fact]
    public async Task PasswordChangeRequiresConfirmedEmailAndOneTimeStepUp()
    {
        await using var postgres = new PostgreSqlBuilder(
                "postgres:17-alpine")
            .Build();
        await postgres.StartAsync();

        await using var app = await TestApplication.CreateAsync(
            postgres.GetConnectionString());
        using var client = app.CreateClient(
            allowAutoRedirect: false);
        const string email = "step-up-alice@example.test";
        const string currentPassword =
            "correct horse battery staple";
        const string newPassword =
            "new correct horse battery staple";

        using var registration = await client.PostAsJsonAsync(
            "/auth/register",
            new
            {
                userName = "step-up-alice",
                email,
                phone = (string?)null,
                profile = new
                {
                    displayName = "Step-up Alice",
                    locale = "en",
                },
                password = currentPassword,
            });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var registered = await registration.Content.ReadFromJsonAsync<
            AccountResponse<IntegrationProfile>>();
        Assert.NotNull(registered);

        var unconfirmedLogin = await LoginAsync(
            client,
            email,
            currentPassword);
        using var unconfirmedChallenge = new HttpRequestMessage(
            HttpMethod.Post,
            "/account/password/change/challenge");
        unconfirmedChallenge.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                unconfirmedLogin.AccessToken);
        using var rejected =
            await client.SendAsync(unconfirmedChallenge);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);

        using var confirmationRequest =
            await client.PostAsJsonAsync(
                "/auth/email-confirmation/request",
                new { email });
        Assert.Equal(
            HttpStatusCode.Accepted,
            confirmationRequest.StatusCode);
        var confirmationMessage =
            await app.WaitForMessageAsync(
                HelloAccountMessageKind.EmailConfirmation);
        var confirmationActionUrl = Assert.IsType<Uri>(
            confirmationMessage.ActionUrl);
        var confirmationQuery = QueryHelpers.ParseQuery(
            confirmationActionUrl.Query);
        using var confirmation = await client.PostAsJsonAsync(
            "/auth/email-confirmation/confirm",
            new
            {
                userId = Guid.Parse(
                    confirmationQuery["userId"].Single()!),
                email = confirmationQuery["email"].Single(),
                token = confirmationQuery["token"].Single(),
            });
        Assert.Equal(
            HttpStatusCode.NoContent,
            confirmation.StatusCode);

        var login = await LoginAsync(
            client,
            email,
            currentPassword);
        using var challengeRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/account/password/change/challenge");
        challengeRequest.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                login.AccessToken);
        using var challengeResponse =
            await client.SendAsync(challengeRequest);
        Assert.Equal(
            HttpStatusCode.OK,
            challengeResponse.StatusCode);
        var challengeJson =
            await challengeResponse.Content.ReadAsStringAsync();
        using var challengeDocument =
            JsonDocument.Parse(challengeJson);
        var challengeId = challengeDocument.RootElement
            .GetProperty("challengeId")
            .GetGuid();
        Assert.Equal(
            "email",
            challengeDocument.RootElement
                .GetProperty("deliveryChannel")
                .GetString());

        var verificationMessage = Assert.Single(
            app.Messages,
            message =>
                message.Kind
                == HelloAccountMessageKind.PasswordChangeVerification);
        Assert.Equal(email, verificationMessage.RecipientAddress);
        Assert.Null(verificationMessage.ActionUrl);
        var verificationCode = Assert.IsType<string>(
            verificationMessage.VerificationCode);
        Assert.Matches("^[0-9]{6}$", verificationCode);
        Assert.DoesNotContain(
            verificationCode,
            challengeJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            email,
            challengeJson,
            StringComparison.OrdinalIgnoreCase);
        await app.UpdateProfileAsync(
            registered.Id,
            new IntegrationProfile("Step-up Alice Updated", "en"));
        Assert.Contains(
            "$kid=v2$",
            await app.GetVerificationVerifierAsync(challengeId),
            StringComparison.Ordinal);
        var invalidCode = string.Equals(
            verificationCode,
            "000000",
            StringComparison.Ordinal)
            ? "111111"
            : "000000";

        using var invalidChange = new HttpRequestMessage(
            HttpMethod.Post,
            "/account/password/change")
        {
            Content = JsonContent.Create(
                new
                {
                    challengeId,
                    verificationCode = invalidCode,
                    currentPassword,
                    newPassword,
                }),
        };
        invalidChange.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                login.AccessToken);
        using var invalidChangeResponse =
            await client.SendAsync(invalidChange);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            invalidChangeResponse.StatusCode);

        using var change = new HttpRequestMessage(
            HttpMethod.Post,
            "/account/password/change")
        {
            Content = JsonContent.Create(
                new
                {
                    challengeId,
                    verificationCode,
                    currentPassword,
                    newPassword,
                }),
        };
        change.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                login.AccessToken);
        using var changed = await client.SendAsync(change);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        using var oldSessionRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/account/me");
        oldSessionRequest.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                login.AccessToken);
        using var oldSession =
            await client.SendAsync(oldSessionRequest);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            oldSession.StatusCode);

        using var oldPasswordLogin = await client.PostAsJsonAsync(
            "/auth/login",
            new
            {
                login = email,
                password = currentPassword,
            });
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            oldPasswordLogin.StatusCode);

        var newLogin = await LoginAsync(
            client,
            email,
            newPassword);
        using var replay = new HttpRequestMessage(
            HttpMethod.Post,
            "/account/password/change")
        {
            Content = JsonContent.Create(
                new
                {
                    challengeId,
                    verificationCode,
                    currentPassword = newPassword,
                    newPassword =
                        "another correct horse battery staple",
                }),
        };
        replay.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                newLogin.AccessToken);
        using var replayed = await client.SendAsync(replay);
        Assert.Equal(
            HttpStatusCode.Conflict,
            replayed.StatusCode);
    }

    [Fact]
    public async Task AccountDeletionRequiresStepUpAndRevokesEverySession()
    {
        await using var postgres = new PostgreSqlBuilder(
                "postgres:17-alpine")
            .Build();
        await postgres.StartAsync();

        await using var app = await TestApplication.CreateAsync(
            postgres.GetConnectionString());
        using var client = app.CreateClient(
            allowAutoRedirect: false);
        const string email = "delete-alice@example.test";
        const string password = "correct horse battery staple";

        using var registration = await client.PostAsJsonAsync(
            "/auth/register",
            new
            {
                userName = "delete-alice",
                email,
                phone = (string?)null,
                profile = new
                {
                    displayName = "Delete Alice",
                    locale = "en",
                },
                password,
            });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        using var confirmationRequest = await client.PostAsJsonAsync(
            "/auth/email-confirmation/request",
            new { email });
        Assert.Equal(
            HttpStatusCode.Accepted,
            confirmationRequest.StatusCode);
        var confirmationMessage = await app.WaitForMessageAsync(
            HelloAccountMessageKind.EmailConfirmation);
        var confirmationQuery = QueryHelpers.ParseQuery(
            Assert.IsType<Uri>(confirmationMessage.ActionUrl).Query);
        using var confirmation = await client.PostAsJsonAsync(
            "/auth/email-confirmation/confirm",
            new
            {
                userId = Guid.Parse(
                    confirmationQuery["userId"].Single()!),
                email = confirmationQuery["email"].Single(),
                token = confirmationQuery["token"].Single(),
            });
        Assert.Equal(HttpStatusCode.NoContent, confirmation.StatusCode);

        var firstLogin = await LoginAsync(client, email, password);
        var secondLogin = await LoginAsync(client, email, password);

        using var passwordRemovalChallenge = new HttpRequestMessage(
            HttpMethod.Post,
            "/account/password/remove/challenge");
        passwordRemovalChallenge.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                firstLogin.AccessToken);
        using var removalRejected = await client.SendAsync(
            passwordRemovalChallenge);
        Assert.Equal(
            HttpStatusCode.Conflict,
            removalRejected.StatusCode);
        using (var problem = JsonDocument.Parse(
                   await removalRejected.Content.ReadAsStringAsync()))
        {
            Assert.Equal(
                HelloAccountSecurityActionErrorCodes.LastSignInMethod,
                problem.RootElement.GetProperty("code").GetString());
        }

        using var deleteChallengeRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/account/delete/challenge");
        deleteChallengeRequest.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                firstLogin.AccessToken);
        using var deleteChallengeResponse = await client.SendAsync(
            deleteChallengeRequest);
        Assert.Equal(HttpStatusCode.OK, deleteChallengeResponse.StatusCode);
        using var challengeDocument = JsonDocument.Parse(
            await deleteChallengeResponse.Content.ReadAsStringAsync());
        var challengeId = challengeDocument.RootElement
            .GetProperty("challengeId")
            .GetGuid();
        var verificationMessage = await app.WaitForMessageAsync(
            HelloAccountMessageKind.AccountSecurityVerification);
        var verificationCode = Assert.IsType<string>(
            verificationMessage.VerificationCode);

        using var deleted = await SendAuthorizedJsonAsync(
            client,
            HttpMethod.Delete,
            "/account",
            firstLogin.AccessToken,
            new
            {
                challengeId,
                verificationCode,
            });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        foreach (var accessToken in new[]
                 {
                     firstLogin.AccessToken,
                     secondLogin.AccessToken,
                 })
        {
            using var accountRequest = new HttpRequestMessage(
                HttpMethod.Get,
                "/account/me");
            accountRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
            using var account = await client.SendAsync(accountRequest);
            Assert.Equal(HttpStatusCode.Unauthorized, account.StatusCode);
        }

        using var loginAfterDeletion = await client.PostAsJsonAsync(
            "/auth/login",
            new { login = email, password });
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            loginAfterDeletion.StatusCode);
    }

    [Fact]
    public async Task CompleteRazorUiFlow()
    {
        await using var postgres = new PostgreSqlBuilder(
                "postgres:17-alpine")
            .Build();
        await postgres.StartAsync();

        await using var app = await TestApplication.CreateAsync(
            postgres.GetConnectionString(),
            configureUi: options =>
            {
                options.ApplicationHomeUrl = "/app";
                options.NoticeText = UiNoticeText;
                options.TermsOfServiceUrl = "/terms";
                options.PrivacyPolicyUrl =
                    "https://legal.example.test/privacy";
                options.CustomCssFilePath = Path.Combine(
                    AppContext.BaseDirectory,
                    "integration-custom.css");
                options.Localization.Enabled = true;
                options.Registration.Email =
                    HelloUiRegistrationFieldMode.Required;
                options.Registration.UserName =
                    HelloUiRegistrationFieldMode.Hidden;
                options.Registration.Phone =
                    HelloUiRegistrationFieldMode.Hidden;
            },
            configureAdmin: options =>
                options.RoleManagementEnabled = false);
        using var client = app.CreateClient(
            allowAutoRedirect: false);
        Dictionary<string, string> cookies =
            new(StringComparer.Ordinal);

        using var apiRegistrationWithoutConsent =
            await client.PostAsJsonAsync(
                "/auth/register",
                new
                {
                    userName = "api-without-consent",
                    email = "api-without-consent@example.test",
                    phone = (string?)null,
                    profile = new
                    {
                        displayName = "API Without Consent",
                        locale = "en",
                    },
                    password = "correct horse battery staple",
                });
        Assert.Equal(
            HttpStatusCode.BadRequest,
            apiRegistrationWithoutConsent.StatusCode);
        using (var problem = JsonDocument.Parse(
                   await apiRegistrationWithoutConsent.Content
                       .ReadAsStringAsync()))
        {
            Assert.Equal(
                HelloRegistrationErrors.ConsentRequiredCode,
                problem.RootElement.GetProperty("code").GetString());
            var errors = problem.RootElement.GetProperty("errors");
            Assert.True(errors.TryGetProperty(
                "acceptTermsOfService",
                out _));
            Assert.True(errors.TryGetProperty(
                "acceptPrivacyPolicy",
                out _));
        }

        var apiConsentStartedAt = DateTimeOffset.UtcNow;
        using var apiRegistration = await client.PostAsJsonAsync(
            "/auth/register",
            new
            {
                userName = "api-with-consent",
                email = "api-with-consent@example.test",
                phone = (string?)null,
                profile = new
                {
                    displayName = "API With Consent",
                    locale = "en",
                    registrationConsent = new
                    {
                        termsOfServiceAccepted = false,
                        privacyPolicyAccepted = false,
                        acceptedAt = DateTimeOffset.UnixEpoch,
                    },
                },
                password = "correct horse battery staple",
                acceptTermsOfService = true,
                acceptPrivacyPolicy = true,
            });
        var apiConsentCompletedAt = DateTimeOffset.UtcNow;
        Assert.Equal(HttpStatusCode.Created, apiRegistration.StatusCode);
        Guid apiTargetUserId;
        using (var apiAccountDocument = JsonDocument.Parse(
                   await apiRegistration.Content.ReadAsStringAsync()))
        {
            apiTargetUserId = apiAccountDocument.RootElement
                .GetProperty("id")
                .GetGuid();
            var consent = apiAccountDocument.RootElement
                .GetProperty("profile")
                .GetProperty("registrationConsent");
            Assert.True(consent
                .GetProperty("termsOfServiceAccepted")
                .GetBoolean());
            Assert.True(consent
                .GetProperty("privacyPolicyAccepted")
                .GetBoolean());
            var acceptedAt = consent
                .GetProperty("acceptedAt")
                .GetDateTimeOffset();
            Assert.InRange(
                acceptedAt,
                apiConsentStartedAt,
                apiConsentCompletedAt);
        }

        using var registerPage = await SendAsync(
            client,
            HttpMethod.Get,
            "/hello/register",
            cookies);
        Assert.Equal(HttpStatusCode.OK, registerPage.StatusCode);
        MergeCookies(cookies, registerPage);
        var registerHtml =
            await registerPage.Content.ReadAsStringAsync();
        AssertUiNotice(registerHtml);
        Assert.Contains(
            "/_content/Skopka.Hello.UI/css/hello.css",
            registerHtml,
            StringComparison.Ordinal);
        Assert.Contains(
            SkopkaHelloUiOptions.DefaultCustomCssRequestPath,
            registerHtml,
            StringComparison.Ordinal);
        Assert.Contains(
            "class=\"hello-header\"",
            registerHtml,
            StringComparison.Ordinal);
        Assert.Contains(
            "href=\"/app\"",
            registerHtml,
            StringComparison.Ordinal);
        Assert.Equal(
            2,
            Regex.Count(registerHtml, "href=\"/terms\""));
        Assert.Equal(
            2,
            Regex.Count(
                registerHtml,
                "href=\"https://legal.example.test/privacy\""));
        Assert.Contains(
            "Terms of Service",
            registerHtml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Privacy Policy",
            registerHtml,
            StringComparison.Ordinal);
        var termsConsent = Regex.Match(
            registerHtml,
            "<input[^>]*id=\"Input_AcceptTermsOfService\"[^>]*>",
            RegexOptions.CultureInvariant).Value;
        Assert.Contains("type=\"checkbox\"", termsConsent);
        Assert.Contains("required", termsConsent);
        var privacyConsent = Regex.Match(
            registerHtml,
            "<input[^>]*id=\"Input_AcceptPrivacyPolicy\"[^>]*>",
            RegexOptions.CultureInvariant).Value;
        Assert.Contains("type=\"checkbox\"", privacyConsent);
        Assert.Contains("required", privacyConsent);
        Assert.Contains(
            "id=\"hello-culture\"",
            registerHtml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "class=\"admin-topbar",
            registerHtml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "integration-host-layout",
            registerHtml,
            StringComparison.Ordinal);
        Assert.Contains(
            "name=\"Input.Email\"",
            registerHtml,
            StringComparison.Ordinal);
        var emailInput = Regex.Match(
            registerHtml,
            "<input[^>]*name=\"Input.Email\"[^>]*>",
            RegexOptions.CultureInvariant).Value;
        Assert.Contains(
            "required",
            emailInput,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "name=\"Input.UserName\"",
            registerHtml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "name=\"Input.Phone\"",
            registerHtml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "name=\"Input.Locale\"",
            registerHtml,
            StringComparison.Ordinal);
        var registerToken = ReadInputValue(
            registerHtml,
            "__RequestVerificationToken");

        using var missingRequiredEmail = await SendFormAsync(
            client,
            "/hello/register",
            cookies,
            new Dictionary<string, string>
            {
                ["Input.DisplayName"] = "Browser Alice",
                ["Input.UserName"] = "injected-user",
                ["Input.Phone"] = "+1 555 010 4242",
                ["Input.Locale"] = "ru",
                ["Input.Password"] =
                    "correct horse battery staple",
                ["Input.ConfirmPassword"] =
                    "correct horse battery staple",
                ["Input.AcceptTermsOfService"] = "true",
                ["Input.AcceptPrivacyPolicy"] = "true",
                ["__RequestVerificationToken"] = registerToken,
            });
        Assert.Equal(HttpStatusCode.OK, missingRequiredEmail.StatusCode);
        var missingRequiredEmailHtml =
            await missingRequiredEmail.Content.ReadAsStringAsync();
        Assert.Contains(
            "The Email field is required.",
            missingRequiredEmailHtml,
            StringComparison.Ordinal);
        MergeCookies(cookies, missingRequiredEmail);
        registerToken = ReadInputValue(
            missingRequiredEmailHtml,
            "__RequestVerificationToken");

        using var rejectedPassword = await SendFormAsync(
            client,
            "/hello/register",
            cookies,
            new Dictionary<string, string>
            {
                ["Input.DisplayName"] = "Browser Alice",
                ["Input.Email"] = "browser-alice@example.test",
                ["Input.Password"] = "too short",
                ["Input.ConfirmPassword"] = "too short",
                ["Input.AcceptTermsOfService"] = "true",
                ["Input.AcceptPrivacyPolicy"] = "true",
                ["__RequestVerificationToken"] = registerToken,
            });
        Assert.Equal(HttpStatusCode.OK, rejectedPassword.StatusCode);
        var rejectedPasswordHtml =
            await rejectedPassword.Content.ReadAsStringAsync();
        Assert.Contains(
            "The password must contain at least 15 characters.",
            rejectedPasswordHtml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "The password does not satisfy the configured policy.",
            rejectedPasswordHtml,
            StringComparison.Ordinal);
        MergeCookies(cookies, rejectedPassword);
        registerToken = ReadInputValue(
            rejectedPasswordHtml,
            "__RequestVerificationToken");

        using var missingLegalConsents = await SendFormAsync(
            client,
            "/hello/register",
            cookies,
            new Dictionary<string, string>
            {
                ["Input.DisplayName"] = "Browser Alice",
                ["Input.Email"] = "browser-alice@example.test",
                ["Input.Password"] =
                    "correct horse battery staple",
                ["Input.ConfirmPassword"] =
                    "correct horse battery staple",
                ["__RequestVerificationToken"] = registerToken,
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
        MergeCookies(cookies, missingLegalConsents);
        registerToken = ReadInputValue(
            missingLegalConsentsHtml,
            "__RequestVerificationToken");

        var uiConsentStartedAt = DateTimeOffset.UtcNow;
        using var register = await SendFormAsync(
            client,
            "/hello/register",
            cookies,
            new Dictionary<string, string>
            {
                ["Input.DisplayName"] = "Browser Alice",
                ["Input.Email"] = "browser-alice@example.test",
                ["Input.UserName"] = "browser-alice",
                ["Input.Phone"] = string.Empty,
                ["Input.Locale"] = "en",
                ["Input.Password"] =
                    "correct horse battery staple",
                ["Input.ConfirmPassword"] =
                    "correct horse battery staple",
                ["Input.AcceptTermsOfService"] = "true",
                ["Input.AcceptPrivacyPolicy"] = "true",
                ["__RequestVerificationToken"] = registerToken,
            });
        var uiConsentCompletedAt = DateTimeOffset.UtcNow;
        Assert.Equal(HttpStatusCode.Redirect, register.StatusCode);
        Assert.StartsWith(
            "/hello/login",
            register.Headers.Location?.OriginalString,
            StringComparison.Ordinal);
        MergeCookies(cookies, register);

        using var confirmationRequest =
            await client.PostAsJsonAsync(
                "/auth/email-confirmation/request",
                new { email = "browser-alice@example.test" });
        Assert.Equal(
            HttpStatusCode.Accepted,
            confirmationRequest.StatusCode);
        var confirmationMessage =
            await app.WaitForMessageAsync(
                HelloAccountMessageKind.EmailConfirmation);
        var confirmationActionUrl = Assert.IsType<Uri>(
            confirmationMessage.ActionUrl);
        var confirmationQuery = QueryHelpers.ParseQuery(
            confirmationActionUrl.Query);
        using var confirmation = await client.PostAsJsonAsync(
            "/auth/email-confirmation/confirm",
            new
            {
                userId = Guid.Parse(
                    confirmationQuery["userId"].Single()!),
                email = confirmationQuery["email"].Single(),
                token = confirmationQuery["token"].Single(),
            });
        Assert.Equal(
            HttpStatusCode.NoContent,
            confirmation.StatusCode);

        using var consentProofLogin = await client.PostAsJsonAsync(
            "/auth/login",
            new
            {
                login = "browser-alice@example.test",
                password = "correct horse battery staple",
            });
        Assert.Equal(HttpStatusCode.OK, consentProofLogin.StatusCode);
        using var consentProofLoginDocument = JsonDocument.Parse(
            await consentProofLogin.Content.ReadAsStringAsync());
        var consentProofAccessToken = consentProofLoginDocument.RootElement
            .GetProperty("accessToken")
            .GetString();
        using var consentProofRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/account/me");
        consentProofRequest.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                consentProofAccessToken);
        using var consentProof = await client.SendAsync(
            consentProofRequest);
        Assert.Equal(HttpStatusCode.OK, consentProof.StatusCode);
        using (var consentProofDocument = JsonDocument.Parse(
                   await consentProof.Content.ReadAsStringAsync()))
        {
            var consent = consentProofDocument.RootElement
                .GetProperty("profile")
                .GetProperty("registrationConsent");
            Assert.True(consent
                .GetProperty("termsOfServiceAccepted")
                .GetBoolean());
            Assert.True(consent
                .GetProperty("privacyPolicyAccepted")
                .GetBoolean());
            Assert.InRange(
                consent.GetProperty("acceptedAt").GetDateTimeOffset(),
                uiConsentStartedAt,
                uiConsentCompletedAt);
        }

        using var loginPage = await SendAsync(
            client,
            HttpMethod.Get,
            "/hello/login",
            cookies);
        Assert.Equal(HttpStatusCode.OK, loginPage.StatusCode);
        MergeCookies(cookies, loginPage);
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        AssertUiNotice(loginHtml);
        Assert.DoesNotContain(
            "Input.Handle",
            loginHtml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Email, phone, or user name",
            loginHtml,
            StringComparison.Ordinal);
        var loginToken = ReadInputValue(
            loginHtml,
            "__RequestVerificationToken");

        using var login = await SendFormAsync(
            client,
            "/hello/login",
            cookies,
            new Dictionary<string, string>
            {
                ["Input.Login"] =
                    "browser-alice@example.test",
                ["Input.Password"] =
                    "correct horse battery staple",
                ["__RequestVerificationToken"] = loginToken,
            });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(
            "/hello/account",
            login.Headers.Location?.OriginalString);
        MergeCookies(cookies, login);
        Assert.True(cookies.ContainsKey(UiCookieName));
        Assert.True(cookies.ContainsKey(RefreshCookieName));

        using var currentUiUser = await SendAsync(
            client,
            HttpMethod.Get,
            "/integration/current-ui-user",
            cookies);
        Assert.Equal(HttpStatusCode.OK, currentUiUser.StatusCode);
        var uiUser = await currentUiUser.Content
            .ReadFromJsonAsync<HelloUiUser>();
        Assert.NotNull(uiUser);
        Assert.NotEqual(Guid.Empty, uiUser.UserId);
        Assert.NotEqual(Guid.Empty, uiUser.SessionId);
        Assert.Equal("Browser Alice", uiUser.DisplayName);

        using var deniedUiAdministrator = await SendAsync(
            client,
            HttpMethod.Get,
            "/integration/ui-administrator",
            cookies);
        Assert.Equal(
            HttpStatusCode.Redirect,
            deniedUiAdministrator.StatusCode);

        var administratorRole =
            await app.GrantAdministratorAsync(uiUser.UserId);
        using var allowedUiAdministrator = await SendAsync(
            client,
            HttpMethod.Get,
            "/integration/ui-administrator",
            cookies);
        Assert.Equal(HttpStatusCode.OK, allowedUiAdministrator.StatusCode);

        var teacherRole = await app.CreateRoleAsync("iq-teacher");
        using var adminRoles = await SendAsync(
            client,
            HttpMethod.Get,
            "/hello/admin/roles",
            cookies);
        Assert.Equal(HttpStatusCode.OK, adminRoles.StatusCode);
        var adminRolesHtml = await adminRoles.Content.ReadAsStringAsync();
        Assert.Contains(
            teacherRole.Name,
            adminRolesHtml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "admin-new-role-title",
            adminRolesHtml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "admin-role-actions",
            adminRolesHtml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "admin-manage-panel",
            adminRolesHtml,
            StringComparison.Ordinal);

        var adminApiLogin = await LoginAsync(
            client,
            "browser-alice@example.test",
            "correct horse battery staple");
        using var rejectedRoleCreation = await SendAuthorizedJsonAsync(
            client,
            HttpMethod.Post,
            "/admin/roles/actions/create/challenge",
            adminApiLogin.AccessToken,
            new { name = "unused-role" });
        Assert.Equal(
            HttpStatusCode.Forbidden,
            rejectedRoleCreation.StatusCode);

        using var adminUsers = await SendAsync(
            client,
            HttpMethod.Get,
            "/hello/admin/users",
            cookies);
        Assert.Equal(HttpStatusCode.OK, adminUsers.StatusCode);
        MergeCookies(cookies, adminUsers);
        var adminUsersHtml =
            await adminUsers.Content.ReadAsStringAsync();
        var administratorCard = ReadAdminUserCard(
            adminUsersHtml,
            uiUser.UserId);
        var administratorRoleSelect = ReadRoleAssignmentSelect(
            administratorCard);
        Assert.Contains(
            teacherRole.Id.ToString("D"),
            administratorRoleSelect,
            StringComparison.Ordinal);
        Assert.Contains(
            teacherRole.Name,
            administratorRoleSelect,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            $"value=\"{administratorRole.Id:D}\"",
            administratorRoleSelect,
            StringComparison.Ordinal);

        var targetCard = ReadAdminUserCard(
            adminUsersHtml,
            apiTargetUserId);
        var targetRoleSelect = ReadRoleAssignmentSelect(targetCard);
        Assert.Contains(
            $"value=\"{teacherRole.Id:D}\"",
            targetRoleSelect,
            StringComparison.Ordinal);
        var adminRoleToken = ReadInputValue(
            adminUsersHtml,
            "__RequestVerificationToken");
        using var beginRoleAssignment = await SendFormAsync(
            client,
            "/hello/admin/users?handler=BeginRoleAction",
            cookies,
            new Dictionary<string, string>
            {
                ["userId"] = apiTargetUserId.ToString("D"),
                ["roleId"] = teacherRole.Id.ToString("D"),
                ["action"] = "assign",
                ["Status"] = IdentityUserStatus.Any.ToString(),
                ["__RequestVerificationToken"] = adminRoleToken,
            });
        Assert.Equal(HttpStatusCode.OK, beginRoleAssignment.StatusCode);
        MergeCookies(cookies, beginRoleAssignment);
        var beginRoleAssignmentHtml =
            await beginRoleAssignment.Content.ReadAsStringAsync();
        Assert.Equal(
            teacherRole.Id,
            Guid.Parse(ReadInputValue(
                beginRoleAssignmentHtml,
                "roleId")));
        var roleChallengeId = Guid.Parse(ReadInputValue(
            beginRoleAssignmentHtml,
            "challengeId"));
        var roleAssignmentToken = ReadInputValue(
            beginRoleAssignmentHtml,
            "__RequestVerificationToken");
        var roleAssignmentMessage = await app.WaitForMessageAsync(
            HelloAccountMessageKind.AdminActionVerification);
        var roleAssignmentCode = Assert.IsType<string>(
            roleAssignmentMessage.VerificationCode);
        using var completeRoleAssignment = await SendFormAsync(
            client,
            "/hello/admin/users?handler=CompleteRoleAction",
            cookies,
            new Dictionary<string, string>
            {
                ["userId"] = apiTargetUserId.ToString("D"),
                ["roleId"] = teacherRole.Id.ToString("D"),
                ["action"] = "assign",
                ["challengeId"] = roleChallengeId.ToString("D"),
                ["verificationCode"] = roleAssignmentCode,
                ["Status"] = IdentityUserStatus.Any.ToString(),
                ["__RequestVerificationToken"] = roleAssignmentToken,
            });
        Assert.Equal(
            HttpStatusCode.Redirect,
            completeRoleAssignment.StatusCode);
        MergeCookies(cookies, completeRoleAssignment);

        using var assignedAdminUsers = await SendAsync(
            client,
            HttpMethod.Get,
            "/hello/admin/users",
            cookies);
        Assert.Equal(HttpStatusCode.OK, assignedAdminUsers.StatusCode);
        MergeCookies(cookies, assignedAdminUsers);
        var assignedAdminUsersHtml =
            await assignedAdminUsers.Content.ReadAsStringAsync();
        var assignedTargetCard = ReadAdminUserCard(
            assignedAdminUsersHtml,
            apiTargetUserId);
        Assert.Contains(
            teacherRole.Name,
            assignedTargetCard,
            StringComparison.Ordinal);
        var assignedTargetRoleSelect = ReadRoleAssignmentSelect(
            assignedTargetCard);
        Assert.DoesNotContain(
            $"value=\"{teacherRole.Id:D}\"",
            assignedTargetRoleSelect,
            StringComparison.Ordinal);

        var selfGrantMessageCount = app.Messages.Count;
        var selfGrantToken = ReadInputValue(
            assignedAdminUsersHtml,
            "__RequestVerificationToken");
        using var beginSelfGrant = await SendFormAsync(
            client,
            "/hello/admin/users?handler=BeginRoleAction",
            cookies,
            new Dictionary<string, string>
            {
                ["userId"] = uiUser.UserId.ToString("D"),
                ["roleId"] = teacherRole.Id.ToString("D"),
                ["action"] = "assign",
                ["Status"] = IdentityUserStatus.Any.ToString(),
                ["__RequestVerificationToken"] = selfGrantToken,
            });
        Assert.Equal(HttpStatusCode.OK, beginSelfGrant.StatusCode);
        MergeCookies(cookies, beginSelfGrant);
        var beginSelfGrantHtml =
            await beginSelfGrant.Content.ReadAsStringAsync();
        var selfGrantChallengeId = Guid.Parse(ReadInputValue(
            beginSelfGrantHtml,
            "challengeId"));
        var selfGrantCompletionToken = ReadInputValue(
            beginSelfGrantHtml,
            "__RequestVerificationToken");
        var selfGrantMessage = Assert.Single(
            app.Messages.Skip(selfGrantMessageCount),
            message => message.Kind
                == HelloAccountMessageKind.AdminActionVerification);
        var selfGrantCode = Assert.IsType<string>(
            selfGrantMessage.VerificationCode);
        using var completeSelfGrant = await SendFormAsync(
            client,
            "/hello/admin/users?handler=CompleteRoleAction",
            cookies,
            new Dictionary<string, string>
            {
                ["userId"] = uiUser.UserId.ToString("D"),
                ["roleId"] = teacherRole.Id.ToString("D"),
                ["action"] = "assign",
                ["challengeId"] = selfGrantChallengeId.ToString("D"),
                ["verificationCode"] = selfGrantCode,
                ["Status"] = IdentityUserStatus.Any.ToString(),
                ["__RequestVerificationToken"] =
                    selfGrantCompletionToken,
            });
        Assert.Equal(HttpStatusCode.Redirect, completeSelfGrant.StatusCode);
        Assert.StartsWith(
            "/hello/admin/users",
            completeSelfGrant.Headers.Location?.OriginalString,
            StringComparison.Ordinal);
        MergeCookies(cookies, completeSelfGrant);

        using var usersAfterSelfGrant = await SendAsync(
            client,
            HttpMethod.Get,
            "/hello/admin/users",
            cookies);
        Assert.Equal(HttpStatusCode.OK, usersAfterSelfGrant.StatusCode);
        MergeCookies(cookies, usersAfterSelfGrant);
        var usersAfterSelfGrantHtml =
            await usersAfterSelfGrant.Content.ReadAsStringAsync();
        Assert.Contains(
            teacherRole.Name,
            ReadAdminUserCard(
                usersAfterSelfGrantHtml,
                uiUser.UserId),
            StringComparison.Ordinal);
        Assert.True(await app.IsUserInRoleAsync(
            uiUser.UserId,
            teacherRole.Id));

        var selfRemoveMessageCount = app.Messages.Count;
        var selfRemoveToken = ReadInputValue(
            usersAfterSelfGrantHtml,
            "__RequestVerificationToken");
        using var beginSelfRemove = await SendFormAsync(
            client,
            "/hello/admin/users?handler=BeginRoleAction",
            cookies,
            new Dictionary<string, string>
            {
                ["userId"] = uiUser.UserId.ToString("D"),
                ["roleId"] = teacherRole.Id.ToString("D"),
                ["action"] = "remove",
                ["Status"] = IdentityUserStatus.Any.ToString(),
                ["__RequestVerificationToken"] = selfRemoveToken,
            });
        Assert.Equal(HttpStatusCode.OK, beginSelfRemove.StatusCode);
        MergeCookies(cookies, beginSelfRemove);
        var beginSelfRemoveHtml =
            await beginSelfRemove.Content.ReadAsStringAsync();
        var selfRemoveChallengeId = Guid.Parse(ReadInputValue(
            beginSelfRemoveHtml,
            "challengeId"));
        var selfRemoveCompletionToken = ReadInputValue(
            beginSelfRemoveHtml,
            "__RequestVerificationToken");
        var selfRemoveMessage = Assert.Single(
            app.Messages.Skip(selfRemoveMessageCount),
            message => message.Kind
                == HelloAccountMessageKind.AdminActionVerification);
        var selfRemoveCode = Assert.IsType<string>(
            selfRemoveMessage.VerificationCode);
        using var completeSelfRemove = await SendFormAsync(
            client,
            "/hello/admin/users?handler=CompleteRoleAction",
            cookies,
            new Dictionary<string, string>
            {
                ["userId"] = uiUser.UserId.ToString("D"),
                ["roleId"] = teacherRole.Id.ToString("D"),
                ["action"] = "remove",
                ["challengeId"] = selfRemoveChallengeId.ToString("D"),
                ["verificationCode"] = selfRemoveCode,
                ["Status"] = IdentityUserStatus.Any.ToString(),
                ["__RequestVerificationToken"] =
                    selfRemoveCompletionToken,
            });
        Assert.Equal(HttpStatusCode.Redirect, completeSelfRemove.StatusCode);
        var selfRemoveLocation = new Uri(
            client.BaseAddress!,
            Assert.IsType<Uri>(completeSelfRemove.Headers.Location));
        Assert.Equal("/hello/login", selfRemoveLocation.AbsolutePath);
        Assert.True(bool.Parse(
            QueryHelpers.ParseQuery(selfRemoveLocation.Query)
                ["rolesChanged"].Single()!));
        MergeCookies(cookies, completeSelfRemove);
        Assert.False(await app.IsUserInRoleAsync(
            uiUser.UserId,
            teacherRole.Id));

        using var rolesChangedLogin = await SendAsync(
            client,
            HttpMethod.Get,
            selfRemoveLocation.PathAndQuery,
            cookies);
        Assert.Equal(HttpStatusCode.OK, rolesChangedLogin.StatusCode);
        MergeCookies(cookies, rolesChangedLogin);
        var rolesChangedLoginHtml =
            await rolesChangedLogin.Content.ReadAsStringAsync();
        Assert.Contains(
            "Your roles changed and your sessions were ended. Sign in again.",
            rolesChangedLoginHtml,
            StringComparison.Ordinal);

        var loginAfterSelfRemoveToken = ReadInputValue(
            rolesChangedLoginHtml,
            "__RequestVerificationToken");
        using var loginAfterSelfRemove = await SendFormAsync(
            client,
            "/hello/login",
            cookies,
            new Dictionary<string, string>
            {
                ["Input.Login"] = "browser-alice@example.test",
                ["Input.Password"] =
                    "correct horse battery staple",
                ["__RequestVerificationToken"] =
                    loginAfterSelfRemoveToken,
            });
        Assert.Equal(HttpStatusCode.Redirect, loginAfterSelfRemove.StatusCode);
        Assert.Equal(
            "/hello/account",
            loginAfterSelfRemove.Headers.Location?.OriginalString);
        MergeCookies(cookies, loginAfterSelfRemove);

        await app.CreateRolesAsync(
            Enumerable.Range(0, 100)
                .Select(index => $"catalog-role-{index:D3}"));
        using var truncatedAdminUsers = await SendAsync(
            client,
            HttpMethod.Get,
            "/hello/admin/users",
            cookies);
        Assert.Equal(HttpStatusCode.OK, truncatedAdminUsers.StatusCode);
        MergeCookies(cookies, truncatedAdminUsers);
        var truncatedAdminUsersHtml =
            await truncatedAdminUsers.Content.ReadAsStringAsync();
        var truncatedTargetCard = ReadAdminUserCard(
            truncatedAdminUsersHtml,
            apiTargetUserId);
        var roleIdInput = Regex.Match(
            truncatedTargetCard,
            $"<input[^>]*id=\"role-id-{apiTargetUserId:N}\"[^>]*>",
            RegexOptions.CultureInvariant).Value;
        Assert.NotEmpty(roleIdInput);
        Assert.Contains("name=\"roleId\"", roleIdInput);
        Assert.Contains(
            $"list=\"role-catalog-{apiTargetUserId:N}\"",
            roleIdInput,
            StringComparison.Ordinal);
        Assert.Contains(
            "Only the first 100 roles are suggested.",
            truncatedTargetCard,
            StringComparison.Ordinal);
        var manualRoleToken = ReadInputValue(
            truncatedAdminUsersHtml,
            "__RequestVerificationToken");
        using var manualRoleAssignment = await SendFormAsync(
            client,
            "/hello/admin/users?handler=BeginRoleAction",
            cookies,
            new Dictionary<string, string>
            {
                ["userId"] = apiTargetUserId.ToString("D"),
                ["roleId"] = administratorRole.Id.ToString("D"),
                ["action"] = "assign",
                ["Status"] = IdentityUserStatus.Any.ToString(),
                ["__RequestVerificationToken"] = manualRoleToken,
            });
        Assert.Equal(HttpStatusCode.OK, manualRoleAssignment.StatusCode);
        var manualRoleAssignmentHtml =
            await manualRoleAssignment.Content.ReadAsStringAsync();
        Assert.Equal(
            administratorRole.Id,
            Guid.Parse(ReadInputValue(
                manualRoleAssignmentHtml,
                "roleId")));

        using var account = await SendAsync(
            client,
            HttpMethod.Get,
            "/hello/account",
            cookies);
        Assert.Equal(HttpStatusCode.OK, account.StatusCode);
        Assert.Contains(
            "no-store",
            account.Headers.CacheControl?.ToString(),
            StringComparison.OrdinalIgnoreCase);
        var accountHtml = await account.Content.ReadAsStringAsync();
        AssertUiNotice(accountHtml);
        Assert.Contains(
            "Browser Alice",
            accountHtml,
            StringComparison.Ordinal);
        Assert.Equal(
            string.Empty,
            ReadInputValue(
                accountHtml,
                "UserNameInput.UserName"));
        var localeInput = Regex.Match(
            accountHtml,
            "<input[^>]*name=\"ProfileValues\\[locale\\]\"[^>]*>",
            RegexOptions.CultureInvariant).Value;
        Assert.NotEmpty(localeInput);
        Assert.DoesNotContain(
            "value=\"ru\"",
            localeInput,
            StringComparison.Ordinal);
        var headerEnd = accountHtml.IndexOf(
            "</header>",
            StringComparison.Ordinal);
        Assert.True(headerEnd >= 0);
        Assert.Contains(
            "handler=Logout",
            accountHtml[..headerEnd],
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "integration-host-layout",
            accountHtml,
            StringComparison.Ordinal);
        MergeCookies(cookies, account);

        var profileVersion = ReadInputValue(
            accountHtml,
            "ProfileExpectedVersion");
        var profileToken = ReadInputValue(
            accountHtml,
            "__RequestVerificationToken");
        using var invalidProfile = await SendFormAsync(
            client,
            "/hello/account?handler=UpdateProfile",
            cookies,
            new Dictionary<string, string>
            {
                ["ProfileExpectedVersion"] = profileVersion,
                ["ProfileValues[displayName]"] = string.Empty,
                ["ProfileValues[locale]"] = "en",
                ["__RequestVerificationToken"] = profileToken,
            });
        Assert.Equal(HttpStatusCode.OK, invalidProfile.StatusCode);
        var invalidProfileHtml =
            await invalidProfile.Content.ReadAsStringAsync();
        Assert.Contains(
            "Display name is required.",
            invalidProfileHtml,
            StringComparison.Ordinal);
        Assert.Contains(
            "class=\"hello-field-error\"",
            invalidProfileHtml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Unmatched profile error.",
            invalidProfileHtml,
            StringComparison.Ordinal);
        MergeCookies(cookies, invalidProfile);

        profileVersion = ReadInputValue(
            invalidProfileHtml,
            "ProfileExpectedVersion");
        profileToken = ReadInputValue(
            invalidProfileHtml,
            "__RequestVerificationToken");
        using var rejectedProfile = await SendFormAsync(
            client,
            "/hello/account?handler=UpdateProfile",
            cookies,
            new Dictionary<string, string>
            {
                ["ProfileExpectedVersion"] = profileVersion,
                ["ProfileValues[displayName]"] =
                    "flat-profile-error",
                ["ProfileValues[locale]"] = "en",
                ["__RequestVerificationToken"] = profileToken,
            });
        Assert.Equal(HttpStatusCode.OK, rejectedProfile.StatusCode);
        var rejectedProfileHtml =
            await rejectedProfile.Content.ReadAsStringAsync();
        Assert.Contains(
            "Profile update failed.",
            rejectedProfileHtml,
            StringComparison.Ordinal);
        MergeCookies(cookies, rejectedProfile);

        profileVersion = ReadInputValue(
            rejectedProfileHtml,
            "ProfileExpectedVersion");
        profileToken = ReadInputValue(
            rejectedProfileHtml,
            "__RequestVerificationToken");
        using var updateProfile = await SendFormAsync(
            client,
            "/hello/account?handler=UpdateProfile",
            cookies,
            new Dictionary<string, string>
            {
                ["ProfileExpectedVersion"] = profileVersion,
                ["ProfileValues[displayName]"] =
                    "Browser Alice Updated",
                ["ProfileValues[locale]"] = "ru",
                ["__RequestVerificationToken"] = profileToken,
            });
        Assert.Equal(HttpStatusCode.Redirect, updateProfile.StatusCode);
        MergeCookies(cookies, updateProfile);

        using var updatedAccount = await SendAsync(
            client,
            HttpMethod.Get,
            "/hello/account",
            cookies);
        var updatedAccountHtml =
            await updatedAccount.Content.ReadAsStringAsync();
        Assert.Contains(
            "Browser Alice Updated",
            updatedAccountHtml,
            StringComparison.Ordinal);
        MergeCookies(cookies, updatedAccount);

        var secondaryLogin = await LoginAsync(
            client,
            "browser-alice@example.test",
            "correct horse battery staple");
        using var sessionsPage = await SendAsync(
            client,
            HttpMethod.Get,
            "/hello/account/sessions",
            cookies);
        Assert.Equal(HttpStatusCode.OK, sessionsPage.StatusCode);
        Assert.Contains(
            "no-store",
            sessionsPage.Headers.CacheControl?.ToString(),
            StringComparison.OrdinalIgnoreCase);
        MergeCookies(cookies, sessionsPage);
        var sessionsHtml =
            await sessionsPage.Content.ReadAsStringAsync();
        var revokeToken = ReadInputValue(
            sessionsHtml,
            "__RequestVerificationToken");

        using var revoke = await SendFormAsync(
            client,
            "/hello/account/sessions?handler=Revoke",
            cookies,
            new Dictionary<string, string>
            {
                ["sessionId"] =
                    secondaryLogin.SessionId.ToString("D"),
                ["__RequestVerificationToken"] = revokeToken,
            });
        Assert.Equal(HttpStatusCode.Redirect, revoke.StatusCode);
        Assert.Equal(
            "/hello/account/sessions",
            revoke.Headers.Location?.OriginalString);
        MergeCookies(cookies, revoke);

        using var securityPage = await SendAsync(
            client,
            HttpMethod.Get,
            "/hello/account/security",
            cookies);
        Assert.Equal(HttpStatusCode.OK, securityPage.StatusCode);
        Assert.Contains(
            "no-store",
            securityPage.Headers.CacheControl?.ToString(),
            StringComparison.OrdinalIgnoreCase);
        var securityHtml = await securityPage.Content.ReadAsStringAsync();
        AssertUiNotice(securityHtml);
        Assert.Contains(
            "A password is configured.",
            securityHtml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Link another sign-in method",
            securityHtml,
            StringComparison.Ordinal);
        MergeCookies(cookies, securityPage);

        using var changePage = await SendAsync(
            client,
            HttpMethod.Get,
            "/hello/account/change-password",
            cookies);
        Assert.Equal(HttpStatusCode.OK, changePage.StatusCode);
        Assert.Contains(
            "no-store",
            changePage.Headers.CacheControl?.ToString(),
            StringComparison.OrdinalIgnoreCase);
        MergeCookies(cookies, changePage);
        var requestCodeHtml =
            await changePage.Content.ReadAsStringAsync();
        var requestCodeToken = ReadInputValue(
            requestCodeHtml,
            "__RequestVerificationToken");

        using var requestCode = await SendFormAsync(
            client,
            "/hello/account/change-password?handler=RequestCode",
            cookies,
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] =
                    requestCodeToken,
            });
        Assert.Equal(HttpStatusCode.OK, requestCode.StatusCode);
        MergeCookies(cookies, requestCode);
        var changeHtml =
            await requestCode.Content.ReadAsStringAsync();
        var challengeId = ReadInputValue(
            changeHtml,
            "Input.ChallengeId");
        var changeToken = ReadInputValue(
            changeHtml,
            "__RequestVerificationToken");
        var verificationMessage = Assert.Single(
            app.Messages,
            message =>
                message.Kind
                == HelloAccountMessageKind.PasswordChangeVerification);
        var verificationCode = Assert.IsType<string>(
            verificationMessage.VerificationCode);

        using var changedPassword = await SendFormAsync(
            client,
            "/hello/account/change-password?handler=Change",
            cookies,
            new Dictionary<string, string>
            {
                ["Input.ChallengeId"] = challengeId,
                ["Input.VerificationCode"] = verificationCode,
                ["Input.CurrentPassword"] =
                    "correct horse battery staple",
                ["Input.NewPassword"] =
                    "new correct horse battery staple",
                ["Input.ConfirmPassword"] =
                    "new correct horse battery staple",
                ["__RequestVerificationToken"] = changeToken,
            });
        Assert.Equal(
            HttpStatusCode.Redirect,
            changedPassword.StatusCode);
        Assert.StartsWith(
            "/hello/login",
            changedPassword.Headers.Location?.OriginalString,
            StringComparison.Ordinal);

        await LoginAsync(
            client,
            "browser-alice@example.test",
            "new correct horse battery staple");
    }

    [Fact]
    public async Task UiPrefixAndSelfRegistrationPolicyApplyTogether()
    {
        await using var postgres = new PostgreSqlBuilder(
                "postgres:17-alpine")
            .Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        const string email = "policy-alice@example.test";
        const string password = "correct horse battery staple";

        await using (var enabled = await TestApplication.CreateAsync(
            connectionString,
            uiPathPrefix: "/identity",
            selfRegistrationEnabled: true))
        {
            using var client = enabled.CreateClient(
                allowAutoRedirect: false);

            using var oldLogin = await client.GetAsync("/hello/login");
            Assert.Equal(HttpStatusCode.NotFound, oldLogin.StatusCode);

            using var loginPage = await client.GetAsync(
                "/identity/login");
            Assert.Equal(HttpStatusCode.OK, loginPage.StatusCode);
            var loginHtml = await loginPage.Content.ReadAsStringAsync();
            Assert.DoesNotContain(
                "class=\"hello-notice\"",
                loginHtml,
                StringComparison.Ordinal);
            Assert.Contains(
                "href=\"/identity/register\"",
                loginHtml,
                StringComparison.Ordinal);

            using var registration = await client.PostAsJsonAsync(
                "/auth/register",
                new
                {
                    userName = "policy-alice",
                    email,
                    phone = (string?)null,
                    profile = new
                    {
                        displayName = "Policy Alice",
                        locale = "en",
                    },
                    password,
                });
            Assert.Equal(
                HttpStatusCode.Created,
                registration.StatusCode);

            using var confirmationRequest =
                await client.PostAsJsonAsync(
                    "/auth/email-confirmation/request",
                    new { email });
            Assert.Equal(
                HttpStatusCode.Accepted,
                confirmationRequest.StatusCode);
            var confirmationMessage =
                await enabled.WaitForMessageAsync(
                    HelloAccountMessageKind.EmailConfirmation);
            Assert.Equal(
                "/identity/confirm-email",
                confirmationMessage.ActionUrl?.AbsolutePath);
        }

        await using (var disabled = await TestApplication.CreateAsync(
            connectionString,
            uiPathPrefix: "/identity",
            selfRegistrationEnabled: false,
            configureUi: options =>
                options.NoticeText = String.Empty))
        {
            using var client = disabled.CreateClient(
                allowAutoRedirect: false);

            using var apiRegistration = await client.PostAsJsonAsync(
                "/auth/register",
                new { email = "new@example.test" });
            Assert.Equal(
                HttpStatusCode.NotFound,
                apiRegistration.StatusCode);

            using var passwordRegistration = await client.GetAsync(
                "/identity/register");
            Assert.Equal(
                HttpStatusCode.NotFound,
                passwordRegistration.StatusCode);

            using var externalRegistration = await client.GetAsync(
                "/identity/external/register");
            Assert.Equal(
                HttpStatusCode.NotFound,
                externalRegistration.StatusCode);

            Dictionary<string, string> cookies =
                new(StringComparer.Ordinal);
            using var loginPage = await SendAsync(
                client,
                HttpMethod.Get,
                "/identity/login",
                cookies);
            Assert.Equal(HttpStatusCode.OK, loginPage.StatusCode);
            MergeCookies(cookies, loginPage);
            var loginHtml = await loginPage.Content.ReadAsStringAsync();
            Assert.DoesNotContain(
                "class=\"hello-notice\"",
                loginHtml,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "href=\"/identity/register\"",
                loginHtml,
                StringComparison.Ordinal);
            var token = ReadInputValue(
                loginHtml,
                "__RequestVerificationToken");

            using var login = await SendFormAsync(
                client,
                "/identity/login",
                cookies,
                new Dictionary<string, string>
                {
                    ["Input.Login"] = email,
                    ["Input.Password"] = password,
                    ["__RequestVerificationToken"] = token,
                });
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            Assert.Equal(
                "/identity/account",
                login.Headers.Location?.OriginalString);
        }
    }

    [Fact]
    public async Task LoginOnlyUiPublishesOnePageAndUsesHostRedirects()
    {
        await using var postgres = new PostgreSqlBuilder(
                "postgres:17-alpine")
            .Build();
        await postgres.StartAsync();

        await using var app = await TestApplication.CreateAsync(
            postgres.GetConnectionString(),
            uiPathPrefix: "/identity",
            configureUi: options =>
            {
                options.EnabledPages = HelloUiPages.Login;
                options.AuthenticatedRedirectPath = "/admin";
            });
        using var client = app.CreateClient(
            allowAutoRedirect: false);

        string[] disabledPaths =
        [
            "/identity",
            "/identity/register",
            "/identity/forgot-password",
            "/identity/reset-password",
            "/identity/resend-confirmation",
            "/identity/resend-phone-confirmation",
            "/identity/confirm-email",
            "/identity/confirm-phone",
            "/identity/external/complete",
            "/identity/external/register",
            "/identity/account",
            "/identity/account/sessions",
            "/identity/account/security",
            "/identity/account/change-password",
            "/identity/account/external-logins",
        ];
        foreach (var path in disabledPaths)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        Dictionary<string, string> handlerCookies =
            new(StringComparer.Ordinal);
        using var loginPage = await SendAsync(
            client,
            HttpMethod.Get,
            "/identity/login",
            handlerCookies);
        Assert.Equal(HttpStatusCode.OK, loginPage.StatusCode);
        MergeCookies(handlerCookies, loginPage);
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        Assert.DoesNotContain(
            "Forgot your password?",
            loginHtml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Resend email confirmation",
            loginHtml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Resend phone confirmation",
            loginHtml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ">Register</a>",
            loginHtml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "data-hello-external-providers",
            loginHtml,
            StringComparison.Ordinal);

        var handlerToken = ReadInputValue(
            loginHtml,
            "__RequestVerificationToken");
        using var disabledExternalHandler = await SendFormAsync(
            client,
            "/identity/login?handler=External",
            handlerCookies,
            new Dictionary<string, string>
            {
                ["providerId"] = "disabled-provider",
                ["__RequestVerificationToken"] = handlerToken,
            });
        Assert.Equal(
            HttpStatusCode.NotFound,
            disabledExternalHandler.StatusCode);

        const string email = "login-only@example.test";
        const string password = "correct horse battery staple";
        using var registration = await client.PostAsJsonAsync(
            "/auth/register",
            new
            {
                userName = "login-only",
                email,
                phone = (string?)null,
                profile = new
                {
                    displayName = "Login Only",
                    locale = "en",
                },
                password,
            });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        var defaultCookies = await LoginThroughUiAsync(
            client,
            email,
            password,
            returnUrl: null,
            expectedLocation: "/admin");
        using var authenticatedLogin = await SendAsync(
            client,
            HttpMethod.Get,
            "/identity/login",
            defaultCookies);
        Assert.Equal(
            HttpStatusCode.Redirect,
            authenticatedLogin.StatusCode);
        Assert.Equal(
            "/admin",
            authenticatedLogin.Headers.Location?.OriginalString);

        _ = await LoginThroughUiAsync(
            client,
            email,
            password,
            returnUrl: "/board",
            expectedLocation: "/board");
        _ = await LoginThroughUiAsync(
            client,
            email,
            password,
            returnUrl: "https://example.test/escape",
            expectedLocation: "/admin");
    }

    [Fact]
    public async Task AnonymousMessageCooldownSilentlyDropsBeforeLookup()
    {
        await using var postgres = new PostgreSqlBuilder(
                "postgres:17-alpine")
            .Build();
        await postgres.StartAsync();

        await using var app = await TestApplication.CreateAsync(
            postgres.GetConnectionString());
        using var client = app.CreateClient(
            allowAutoRedirect: false);

        using var first = await client.PostAsJsonAsync(
            "/auth/email-confirmation/request",
            new { email = "missing@example.test" });
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        using var repeated = await client.PostAsJsonAsync(
            "/auth/email-confirmation/request",
            new { email = "MISSING@EXAMPLE.TEST" });
        Assert.Equal(
            HttpStatusCode.Accepted,
            repeated.StatusCode);
        Assert.Empty(app.Messages);
    }

    [Fact]
    public async Task CompleteEmailConfirmationAndPasswordResetFlow()
    {
        await using var postgres = new PostgreSqlBuilder(
                "postgres:17-alpine")
            .Build();
        await postgres.StartAsync();

        await using var app = await TestApplication.CreateAsync(
            postgres.GetConnectionString());
        using var client = app.CreateClient(
            allowAutoRedirect: false);

        using var registration = await client.PostAsJsonAsync(
            "/auth/register",
            new
            {
                userName = "recovery-alice",
                email = "recovery-alice@example.test",
                phone = (string?)null,
                profile = new
                {
                    displayName = "Recovery Alice",
                    locale = "en",
                },
                password = "correct horse battery staple",
            });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        using var invalidRequest = await client.PostAsJsonAsync(
            "/auth/email-confirmation/request",
            new { email = "invalid" });
        Assert.Equal(
            HttpStatusCode.BadRequest,
            invalidRequest.StatusCode);

        using var unknownConfirmation =
            await client.PostAsJsonAsync(
                "/auth/email-confirmation/request",
                new { email = "unknown@example.test" });
        Assert.Equal(
            HttpStatusCode.Accepted,
            unknownConfirmation.StatusCode);
        Assert.Empty(app.Messages);

        using var confirmationRequest =
            await client.PostAsJsonAsync(
                "/auth/email-confirmation/request",
                new { email = "recovery-alice@example.test" });
        Assert.Equal(
            HttpStatusCode.Accepted,
            confirmationRequest.StatusCode);
        var confirmationMessage =
            await app.WaitForMessageAsync(
                HelloAccountMessageKind.EmailConfirmation);
        var confirmationActionUrl = Assert.IsType<Uri>(
            confirmationMessage.ActionUrl);

        var loginBeforeConfirmation = await LoginAsync(
            client,
            "recovery-alice@example.test",
            "correct horse battery staple");
        using var beforeConfirmation = await GetMeAsync(
            client,
            loginBeforeConfirmation.AccessToken);
        Assert.False(ReadEmailConfirmed(beforeConfirmation));

        using var confirmationPage = await client.GetAsync(
            confirmationActionUrl.PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, confirmationPage.StatusCode);
        Assert.Contains(
            "no-store",
            confirmationPage.Headers.CacheControl?.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "no-referrer",
            confirmationPage.Headers
                .GetValues("Referrer-Policy")
                .Single());
        var confirmationHtml =
            await confirmationPage.Content.ReadAsStringAsync();
        Assert.Contains(
            "data-hello-auto-submit=\"true\"",
            confirmationHtml,
            StringComparison.Ordinal);
        Assert.Contains(
            "name=\"__RequestVerificationToken\"",
            confirmationHtml,
            StringComparison.Ordinal);
        Assert.Contains(
            "/_content/Skopka.Hello.UI/js/confirm-email.js",
            confirmationHtml,
            StringComparison.Ordinal);
        Assert.Contains(
            "<noscript>",
            confirmationHtml,
            StringComparison.Ordinal);

        using var stillUnconfirmed = await GetMeAsync(
            client,
            loginBeforeConfirmation.AccessToken);
        Assert.False(ReadEmailConfirmed(stillUnconfirmed));

        var confirmationQuery = QueryHelpers.ParseQuery(
            confirmationActionUrl.Query);
        using var confirmed = await client.PostAsJsonAsync(
            "/auth/email-confirmation/confirm",
            new
            {
                userId = Guid.Parse(
                    confirmationQuery["userId"].Single()!),
                email = confirmationQuery["email"].Single(),
                token = confirmationQuery["token"].Single(),
            });
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);

        using var afterConfirmation = await GetMeAsync(
            client,
            loginBeforeConfirmation.AccessToken);
        Assert.True(ReadEmailConfirmed(afterConfirmation));

        using var repeatedConfirmation =
            await client.PostAsJsonAsync(
                "/auth/email-confirmation/request",
                new { email = "recovery-alice@example.test" });
        Assert.Equal(
            HttpStatusCode.Accepted,
            repeatedConfirmation.StatusCode);
        Assert.Single(
            app.Messages,
            message =>
                message.Kind
                == HelloAccountMessageKind.EmailConfirmation);

        using var unknownReset = await client.PostAsJsonAsync(
            "/auth/password-reset/request",
            new { email = "unknown@example.test" });
        Assert.Equal(HttpStatusCode.Accepted, unknownReset.StatusCode);
        Assert.DoesNotContain(
            app.Messages,
            message =>
                message.Kind
                == HelloAccountMessageKind.PasswordReset);

        using var resetRequest = await client.PostAsJsonAsync(
            "/auth/password-reset/request",
            new { email = "recovery-alice@example.test" });
        Assert.Equal(HttpStatusCode.Accepted, resetRequest.StatusCode);
        var resetMessage = await app.WaitForMessageAsync(
            HelloAccountMessageKind.PasswordReset);
        var resetActionUrl = Assert.IsType<Uri>(
            resetMessage.ActionUrl);

        using var resetPage = await client.GetAsync(
            resetActionUrl.PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, resetPage.StatusCode);

        var loginAfterGet = await LoginAsync(
            client,
            "recovery-alice@example.test",
            "correct horse battery staple");

        var resetQuery = QueryHelpers.ParseQuery(
            resetActionUrl.Query);
        using var reset = await client.PostAsJsonAsync(
            "/auth/password-reset/confirm",
            new
            {
                userId = Guid.Parse(
                    resetQuery["userId"].Single()!),
                token = resetQuery["token"].Single(),
                newPassword = "new correct horse battery staple",
            });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        using var oldPassword = await client.PostAsJsonAsync(
            "/auth/login",
            new
            {
                login = "recovery-alice@example.test",
                password = "correct horse battery staple",
            });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);

        var newPassword = await LoginAsync(
            client,
            "recovery-alice@example.test",
            "new correct horse battery staple");
        Assert.NotEqual(
            loginAfterGet.AccessToken,
            newPassword.AccessToken);
    }

    [Fact]
    public async Task AutomaticPhoneLoginAndPhoneConfirmationFlow()
    {
        await using var postgres = new PostgreSqlBuilder(
                "postgres:17-alpine")
            .Build();
        await postgres.StartAsync();

        await using var app = await TestApplication.CreateAsync(
            postgres.GetConnectionString(),
            verificationChannel: HelloDeliveryChannel.Sms);
        using var client = app.CreateClient(
            allowAutoRedirect: false);
        const string phone = "+1 (202) 555-0123";

        using var registration = await client.PostAsJsonAsync(
            "/auth/register",
            new
            {
                userName = "phone-alice",
                email = (string?)null,
                phone,
                profile = new
                {
                    displayName = "Phone Alice",
                    locale = "en",
                },
                password = "correct horse battery staple",
            });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        var phoneLogin = await LoginAsync(
            client,
            "+1 202 555 0123",
            "correct horse battery staple");
        using var beforeConfirmation = await GetMeAsync(
            client,
            phoneLogin.AccessToken);
        Assert.False(ReadPhoneConfirmed(beforeConfirmation));

        using var collidingRegistration = await client.PostAsJsonAsync(
            "/auth/register",
            new
            {
                userName = "12025550123",
                email = "phone-collision@example.test",
                phone = (string?)null,
                profile = new
                {
                    displayName = "Collision",
                    locale = "en",
                },
                password = "correct horse battery staple",
            });
        Assert.Equal(
            HttpStatusCode.Conflict,
            collidingRegistration.StatusCode);

        using var unknownRequest = await client.PostAsJsonAsync(
            "/auth/phone-confirmation/request",
            new { phone = "+1 202 555 9999" });
        Assert.Equal(
            HttpStatusCode.Accepted,
            unknownRequest.StatusCode);
        Assert.Empty(app.Messages);

        using var confirmationRequest = await client.PostAsJsonAsync(
            "/auth/phone-confirmation/request",
            new { phone = "+1 202 555 0123" });
        Assert.Equal(
            HttpStatusCode.Accepted,
            confirmationRequest.StatusCode);
        var confirmationMessage =
            await app.WaitForMessageAsync(
                HelloAccountMessageKind.PhoneConfirmation);
        Assert.Equal(
            HelloDeliveryChannel.Sms,
            confirmationMessage.Channel);
        Assert.Equal(phone, confirmationMessage.RecipientAddress);
        var confirmationActionUrl = Assert.IsType<Uri>(
            confirmationMessage.ActionUrl);

        using var confirmationPage = await client.GetAsync(
            confirmationActionUrl.PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, confirmationPage.StatusCode);
        Assert.Contains(
            "no-store",
            confirmationPage.Headers.CacheControl?.ToString(),
            StringComparison.OrdinalIgnoreCase);

        using var stillUnconfirmed = await GetMeAsync(
            client,
            phoneLogin.AccessToken);
        Assert.False(ReadPhoneConfirmed(stillUnconfirmed));

        var confirmationQuery = QueryHelpers.ParseQuery(
            confirmationActionUrl.Query);
        using var confirmed = await client.PostAsJsonAsync(
            "/auth/phone-confirmation/confirm",
            new
            {
                userId = Guid.Parse(
                    confirmationQuery["userId"].Single()!),
                phone = confirmationQuery["phone"].Single(),
                token = confirmationQuery["token"].Single(),
            });
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);

        using var afterConfirmation = await GetMeAsync(
            client,
            phoneLogin.AccessToken);
        Assert.True(ReadPhoneConfirmed(afterConfirmation));

        using var repeatedRequest = await client.PostAsJsonAsync(
            "/auth/phone-confirmation/request",
            new { phone });
        Assert.Equal(
            HttpStatusCode.Accepted,
            repeatedRequest.StatusCode);
        Assert.Single(
            app.Messages,
            message =>
                message.Kind
                == HelloAccountMessageKind.PhoneConfirmation);

        using var challengeRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/account/password/change/challenge");
        challengeRequest.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                phoneLogin.AccessToken);
        using var challengeResponse = await client.SendAsync(
            challengeRequest);
        Assert.Equal(HttpStatusCode.OK, challengeResponse.StatusCode);
        using var challengeDocument = JsonDocument.Parse(
            await challengeResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            "sms",
            challengeDocument.RootElement
                .GetProperty("deliveryChannel")
                .GetString());
        var verificationMessage = Assert.Single(
            app.Messages,
            message =>
                message.Kind
                == HelloAccountMessageKind.PasswordChangeVerification);
        Assert.Equal(
            HelloDeliveryChannel.Sms,
            verificationMessage.Channel);
        Assert.Equal(phone, verificationMessage.RecipientAddress);

        _ = await LoginAsync(
            client,
            "phone-alice",
            "correct horse battery staple");
    }

    [Fact]
    public async Task PasswordFailuresUseVersionedPersistentRateLimiting()
    {
        await using var postgres = new PostgreSqlBuilder(
                "postgres:17-alpine")
            .Build();
        await postgres.StartAsync();

        await using var app = await TestApplication.CreateAsync(
            postgres.GetConnectionString());
        using var client = app.CreateClient();
        using var registration = await client.PostAsJsonAsync(
            "/auth/register",
            new
            {
                userName = "limited-alice",
                email = "limited-alice@example.test",
                phone = (string?)null,
                profile = new
                {
                    displayName = "Limited Alice",
                    locale = "en",
                },
                password = "correct horse battery staple",
            });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var rejected = await client.PostAsJsonAsync(
                "/auth/login",
                new
                {
                    login = "limited-alice@example.test",
                    password =
                        "incorrect horse battery staple",
                });
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                rejected.StatusCode);
        }

        using var limited = await client.PostAsJsonAsync(
            "/auth/login",
            new
            {
                login = "limited-alice@example.test",
                password = "correct horse battery staple",
            });

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            limited.StatusCode);
        using var problem = JsonDocument.Parse(
            await limited.Content.ReadAsStringAsync());
        Assert.Equal(
            IdentityErrorCodes.RateLimitExceeded,
            problem.RootElement.GetProperty("code").GetString());
        Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.Equal(
            ["v1", "v2"],
            await app.GetRateLimitVersionsAsync(
                "password.account"));
    }

    private static async Task<HttpResponseMessage> SendFormAsync(
        HttpClient client,
        string path,
        IReadOnlyDictionary<string, string> cookies,
        IReadOnlyDictionary<string, string> form)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            path)
        {
            Content = new FormUrlEncodedContent(form),
        };
        AddCookies(request, cookies);
        return await client.SendAsync(request);
    }

    private static async Task<Dictionary<string, string>>
        LoginThroughUiAsync(
            HttpClient client,
            string login,
            string password,
            string? returnUrl,
            string expectedLocation)
    {
        Dictionary<string, string> cookies =
            new(StringComparer.Ordinal);
        var pagePath = returnUrl is null
            ? "/identity/login"
            : "/identity/login?ReturnUrl="
                + Uri.EscapeDataString(returnUrl);
        using var loginPage = await SendAsync(
            client,
            HttpMethod.Get,
            pagePath,
            cookies);
        Assert.Equal(HttpStatusCode.OK, loginPage.StatusCode);
        MergeCookies(cookies, loginPage);
        var html = await loginPage.Content.ReadAsStringAsync();
        var token = ReadInputValue(
            html,
            "__RequestVerificationToken");

        var form = new Dictionary<string, string>
        {
            ["Input.Login"] = login,
            ["Input.Password"] = password,
            ["__RequestVerificationToken"] = token,
        };
        if (returnUrl is not null)
        {
            form["ReturnUrl"] = returnUrl;
        }

        using var response = await SendFormAsync(
            client,
            "/identity/login",
            cookies,
            form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            expectedLocation,
            response.Headers.Location?.OriginalString);
        MergeCookies(cookies, response);
        return cookies;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string> cookies)
    {
        var request = new HttpRequestMessage(method, path);
        AddCookies(request, cookies);
        return await client.SendAsync(request);
    }

    private static void AddCookies(
        HttpRequestMessage request,
        IReadOnlyDictionary<string, string> cookies)
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
        if (!response.Headers.TryGetValues(
                "Set-Cookie",
                out var values))
        {
            return;
        }

        foreach (var value in values)
        {
            var pair = value.Split(';', 2)[0];
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            cookies[pair[..separator]] =
                pair[(separator + 1)..];
        }
    }

    private static void AssertUiNotice(string html)
    {
        var matches = Regex.Matches(
            html,
            "<div class=\"hello-notice\">(.*?)</div>",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);
        var match = Assert.Single(matches.Cast<Match>());
        Assert.Equal(
            UiNoticeText,
            WebUtility.HtmlDecode(match.Groups[1].Value));
        Assert.DoesNotContain(
            "<data>",
            match.Value,
            StringComparison.Ordinal);
    }

    private static string ReadAdminUserCard(string html, Guid userId)
    {
        var marker = $"id=\"admin-user-{userId:N}\"";
        var markerIndex = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(
            markerIndex >= 0,
            $"Admin card for user '{userId:D}' was not found.");
        var start = html.LastIndexOf(
            "<article",
            markerIndex,
            StringComparison.Ordinal);
        var end = html.IndexOf(
            "</article>",
            markerIndex,
            StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return html[start..(end + "</article>".Length)];
    }

    private static string ReadRoleAssignmentSelect(string userCard)
    {
        var match = Regex.Match(
            userCard,
            "<select[^>]*name=\"roleId\"[^>]*>.*?</select>",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);
        Assert.True(
            match.Success,
            "The role assignment select was not found.");
        return match.Value;
    }

    private static string ReadInputValue(
        string html,
        string name)
    {
        var match = Regex.Match(
            html,
            $"<input[^>]*name=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]*)\"",
            RegexOptions.CultureInvariant);
        Assert.True(
            match.Success,
            $"Input '{name}' was not found in the rendered page.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> SendAuthorizedJsonAsync(
        HttpClient client,
        HttpMethod method,
        string requestUri,
        string accessToken,
        object body)
    {
        using var request = new HttpRequestMessage(method, requestUri)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        return await client.SendAsync(request);
    }

    private static async Task<LoginResult> LoginAsync(
        HttpClient client,
        string login = "alice@example.test",
        string password = "correct horse battery staple")
    {
        using var response = await client.PostAsJsonAsync(
            "/auth/login",
            new
            {
                login,
                password,
            });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<
            SessionPayload>();
        Assert.NotNull(payload);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        Assert.False(
            document.RootElement.TryGetProperty(
                "refreshToken",
                out _));

        var cookies = CookieSet.FromResponse(response);
        var refreshHeader = response.Headers
            .GetValues("Set-Cookie")
            .Single(value => value.StartsWith(
                $"{RefreshCookieName}=",
                StringComparison.Ordinal));

        return new LoginResult(
            payload.AccessToken,
            payload.SessionId,
            cookies,
            refreshHeader);
    }

    private static async Task<JsonDocument> GetMeAsync(
        HttpClient client,
        string accessToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/account/me");
        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
    }

    private static bool ReadEmailConfirmed(JsonDocument account)
        => account.RootElement
            .GetProperty("emailConfirmed")
            .GetBoolean();

    private static bool ReadPhoneConfirmed(JsonDocument account)
        => account.RootElement
            .GetProperty("phoneConfirmed")
            .GetBoolean();

    private static async Task<LoginResult> RefreshAsync(
        HttpClient client,
        CookieSet cookies)
    {
        using var request = CreateCookieMutation(
            HttpMethod.Post,
            "/auth/refresh",
            cookies);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<
            SessionPayload>();
        Assert.NotNull(payload);
        return new LoginResult(
            payload.AccessToken,
            payload.SessionId,
            CookieSet.FromResponse(response, cookies),
            response.Headers
                .GetValues("Set-Cookie")
                .Single(value => value.StartsWith(
                    $"{RefreshCookieName}=",
                    StringComparison.Ordinal)));
    }

    private static HttpRequestMessage CreateCookieMutation(
        HttpMethod method,
        string path,
        CookieSet cookies)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            cookies.ToRequestHeader());
        request.Headers.TryAddWithoutValidation(
            AntiforgeryHeaderName,
            cookies.AntiforgeryRequestToken);
        return request;
    }

    private sealed record IntegrationProfile(
        string DisplayName,
        string? Locale)
    {
        public IntegrationRegistrationConsent? RegistrationConsent
        {
            get;
            init;
        }
    }

    private sealed record IntegrationRegistrationConsent(
        bool TermsOfServiceAccepted,
        bool PrivacyPolicyAccepted,
        DateTimeOffset AcceptedAt);

    private sealed class IntegrationProfileUiFactory
        : IHelloUiProfileFactory<IntegrationProfile>,
            IHelloUiProfileEditor<IntegrationProfile>,
            IHelloRegistrationConsentProfileEnricher<IntegrationProfile>
    {
        public OperationResult<IntegrationProfile> Create(
            HelloUiRegistrationProfile profile)
            => OperationResultFactory.Success(
                new IntegrationProfile(
                    profile.DisplayName,
                    profile.Locale)
                {
                    RegistrationConsent = ToProfileConsent(
                        profile.RegistrationConsent),
                });

        public string GetDisplayName(
            IntegrationProfile profile)
            => profile.DisplayName;

        public IReadOnlyList<HelloUiProfileField> GetFields(
            IntegrationProfile profile)
            =>
            [
                new HelloUiProfileField(
                    "displayName",
                    "Display name",
                    profile.DisplayName,
                    Required: true),
                new HelloUiProfileField(
                    "locale",
                    "Locale",
                    profile.Locale),
            ];

        public OperationResult<IntegrationProfile> Update(
            IntegrationProfile current,
            IReadOnlyDictionary<string, string?> values)
        {
            values.TryGetValue("displayName", out var displayName);
            values.TryGetValue("locale", out var locale);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return OperationResultFactory.Fail<IntegrationProfile>(
                    new Error(
                        "integration.profile.invalid",
                        "Profile validation failed.",
                        ErrorType.Validation,
                        new ValidationDetails(
                            new Dictionary<string, string[]>
                            {
                                ["displayName"] =
                                ["Display name is required."],
                                ["notRendered"] =
                                ["Unmatched profile error."],
                            })));
            }

            if (string.Equals(
                    displayName,
                    "flat-profile-error",
                    StringComparison.Ordinal))
            {
                return OperationResultFactory.Fail<IntegrationProfile>(
                    new Error(
                        "integration.profile.rejected",
                        "Profile update failed.",
                        ErrorType.Validation));
            }

            return OperationResultFactory.Success(
                new IntegrationProfile(
                    displayName,
                    locale)
                {
                    RegistrationConsent =
                        current.RegistrationConsent,
                });
        }

        public OperationResult<IntegrationProfile> Enrich(
            IntegrationProfile profile,
            HelloRegistrationConsent consent)
            => OperationResultFactory.Success(
                profile with
                {
                    RegistrationConsent = ToProfileConsent(consent),
                });

        private static IntegrationRegistrationConsent? ToProfileConsent(
            HelloRegistrationConsent? consent)
            => consent is { AcceptedAt: { } acceptedAt }
                ? new IntegrationRegistrationConsent(
                    consent.TermsOfServiceAccepted,
                    consent.PrivacyPolicyAccepted,
                    acceptedAt)
                : null;
    }

    private sealed class IntegrationAdminProfileProjector
        : IHelloAdminProfileProjector<IntegrationProfile>
    {
        public Task<OperationResult<IReadOnlyList<HelloAdminProfileField>>>
            ProjectAsync(
                IntegrationProfile profile,
                HelloAdminProfileProjectionContext context,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<HelloAdminProfileField> fields =
            [
                new("displayName", "Display name", profile.DisplayName),
            ];
            return Task.FromResult(OperationResultFactory.Success(fields));
        }
    }

    private sealed record SessionPayload(
        Guid SessionId,
        string AccessToken,
        DateTimeOffset AccessTokenExpiresAt,
        DateTimeOffset RefreshTokenExpiresAt);

    private sealed record LoginResult(
        string AccessToken,
        Guid SessionId,
        CookieSet Cookies,
        string RefreshSetCookie);

    private sealed record CookieSet(
        string RefreshToken,
        string AntiforgeryCookie,
        string AntiforgeryRequestToken)
    {
        public static CookieSet FromResponse(
            HttpResponseMessage response,
            CookieSet? fallback = null)
        {
            var values = response.Headers
                .GetValues("Set-Cookie")
                .ToArray();
            return new CookieSet(
                Read(
                    values,
                    RefreshCookieName,
                    fallback?.RefreshToken),
                Read(
                    values,
                    AntiforgeryCookieName,
                    fallback?.AntiforgeryCookie),
                Read(
                    values,
                    AntiforgeryRequestCookieName,
                    fallback?.AntiforgeryRequestToken));
        }

        public string ToRequestHeader()
            => $"{RefreshCookieName}={RefreshToken}; "
                + $"{AntiforgeryCookieName}={AntiforgeryCookie}; "
                + $"{AntiforgeryRequestCookieName}={AntiforgeryRequestToken}";

        private static string Read(
            IEnumerable<string> values,
            string name,
            string? fallback)
        {
            var prefix = $"{name}=";
            var header = values.SingleOrDefault(value =>
                value.StartsWith(
                    prefix,
                    StringComparison.Ordinal));
            if (header is null)
            {
                return fallback
                    ?? throw new InvalidOperationException(
                        $"Cookie '{name}' was not issued.");
            }

            var separator = header.IndexOf(
                ';',
                prefix.Length);
            return separator < 0
                ? header[prefix.Length..]
                : header[prefix.Length..separator];
        }
    }

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

            return Task.FromResult(
                OperationResultFactory.Success());
        }
    }

    private sealed class TestApplication : IAsyncDisposable
    {
        private readonly WebApplication application;
        private readonly RecordingAccountMessageSender messageSender;

        private TestApplication(
            WebApplication application,
            RecordingAccountMessageSender messageSender)
        {
            this.application = application;
            this.messageSender = messageSender;
        }

        public IReadOnlyList<HelloAccountMessage> Messages =>
            messageSender.Messages;

        public async Task<HelloAccountMessage> WaitForMessageAsync(
            HelloAccountMessageKind kind)
        {
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(10));
            while (!timeout.IsCancellationRequested)
            {
                var matches = Messages
                    .Where(message => message.Kind == kind)
                    .ToArray();
                if (matches.Length > 0)
                {
                    return Assert.Single(matches);
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

        public static async Task<TestApplication> CreateAsync(
            string connectionString,
            string uiPathPrefix = "/hello",
            bool selfRegistrationEnabled = true,
            HelloDeliveryChannel verificationChannel =
                HelloDeliveryChannel.Email,
            Action<SkopkaHelloUiOptions>? configureUi = null,
            Action<SkopkaHelloAdminOptions>? configureAdmin = null,
            Action<SkopkaHelloOptions>? configureHello = null)
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    ApplicationName = typeof(AuthenticationFlowTests)
                        .Assembly.GetName().Name,
                    EnvironmentName = "IntegrationTests",
                });
            builder.WebHost.ConfigureKestrel(options =>
                options.Listen(
                    IPAddress.Loopback,
                    0));
            var messageSender =
                new RecordingAccountMessageSender();
            builder.Services.AddSingleton<
                IHelloAccountMessageSender>(messageSender);

            var identity = builder.Services
                .AddSkopkaHello<IntegrationProfile>(options =>
                {
                    options.PublicOrigin = new Uri(
                        "https://integration.skopka.test");
                    options.UiPathPrefix = uiPathPrefix;
                    options.SelfRegistrationEnabled =
                        selfRegistrationEnabled;
                    configureHello?.Invoke(options);
                })
                .ConfigurePasswordPolicy(options =>
                {
                    options.MinimumLength = 15;
                    options.MaximumLength = 128;
                })
                .UsePostgreSql(connectionString)
                .UsePbkdf2PasswordHasher(options =>
                {
                    options.Iterations = 1_000;
                    options.MaximumAcceptedIterations = 1_000;
                })
                .UseDataProtectionActionTokens();
            var jwtKeys = new Dictionary<string, byte[]>
            {
                ["v1"] = RandomNumberGenerator.GetBytes(32),
                ["v2"] = RandomNumberGenerator.GetBytes(32),
            };
            try
            {
                identity.UseJwtSessions(
                    "v2",
                    jwtKeys,
                    options =>
                    {
                        options.Issuer =
                            "https://integration.skopka.test";
                        options.Audience =
                            "skopka-hello-integration";
                    });
            }
            finally
            {
                foreach (var key in jwtKeys.Values)
                {
                    CryptographicOperations.ZeroMemory(key);
                }
            }

            builder.Services.AddSkopkaHelloDelivery(options =>
                options.VerificationChannel = verificationChannel);
            var rateLimitKeys = new Dictionary<string, byte[]>
            {
                ["v1"] = RandomNumberGenerator.GetBytes(32),
                ["v2"] = RandomNumberGenerator.GetBytes(32),
            };
            try
            {
                identity.UseHmacRateLimiting(
                    "v2",
                    rateLimitKeys);
            }
            finally
            {
                foreach (var key in rateLimitKeys.Values)
                {
                    CryptographicOperations.ZeroMemory(key);
                }
            }

            var verificationKeys =
                new Dictionary<string, byte[]>
                {
                    ["v1"] = RandomNumberGenerator.GetBytes(32),
                    ["v2"] = RandomNumberGenerator.GetBytes(32),
                };
            try
            {
                var verificationKeyProvider =
                    new StaticVerificationCodeKeyProvider(
                        "v2",
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

            identity.AddRoles();
            identity.UseJwtBearerAuthentication();
            builder.Services.AddProblemDetails();
            builder.Services.AddSkopkaHelloUi<
                IntegrationProfile,
                IntegrationProfileUiFactory>(configureUi);
            builder.Services.AddSkopkaHelloAdmin<
                IntegrationProfile,
                IntegrationAdminProfileProjector>(configureAdmin);
            builder.Services.AddSkopkaHelloCurrentRolePolicy<
                IntegrationProfile>(
                UiAdministratorPolicy,
                HelloAdminDefaults.AdministratorRole,
                HelloUiDefaults.AuthenticationScheme);

            var application = builder.Build();
            application.UseExceptionHandler();
            application.UseStatusCodePages();
            application.Use(
                static (context, next) =>
                {
                    context.Request.Scheme = "https";
                    return next(context);
                });
            application.UseAuthentication();
            application.UseAuthorization();
            application.MapSkopkaHello<IntegrationProfile>();
            application.MapSkopkaHelloAdmin<IntegrationProfile>();
            application.MapSkopkaHelloUi();
            application.MapGet(
                    "/integration/current-ui-user",
                    async (
                        HttpContext httpContext,
                        IHelloUiUserAccessor accessor,
                        CancellationToken cancellationToken) =>
                    {
                        var user = await accessor.GetAsync(
                            httpContext,
                            cancellationToken);
                        return user is null
                            ? Results.Unauthorized()
                            : Results.Ok(user);
                    })
                .RequireAuthorization(
                    HelloUiDefaults.AuthorizationPolicy);
            application.MapGet(
                    "/integration/ui-administrator",
                    static () => Results.Ok())
                .RequireAuthorization(UiAdministratorPolicy);

            await using (var scope =
                application.Services.CreateAsyncScope())
            {
                var database =
                    scope.ServiceProvider.GetRequiredService<
                        PostgreSqlIdentityDbContext<
                            IntegrationProfile>>();
                await database.Database.MigrateAsync();
            }

            await application.StartAsync();
            return new TestApplication(
                application,
                messageSender);
        }

        public async Task<IdentityRole> GrantAdministratorAsync(Guid userId)
        {
            await using var scope =
                application.Services.CreateAsyncScope();
            var roles = scope.ServiceProvider.GetRequiredService<
                IIdentityRoleService<IntegrationProfile>>();
            var role = await roles.FindByNameAsync(
                HelloAdminDefaults.AdministratorRole,
                CancellationToken.None);
            if (role is null)
            {
                var created = await roles.CreateAsync(
                    new CreateRoleCommand(
                        HelloAdminDefaults.AdministratorRole),
                    CancellationToken.None);
                Assert.True(created.IsSuccess);
                role = created.Value;
            }

            var assigned = await roles.AssignAsync(
                new AssignRoleCommand(userId, role.Id),
                CancellationToken.None);
            Assert.True(assigned.IsSuccess);
            return role;
        }

        public async Task<IdentityRole> CreateRoleAsync(string name)
        {
            await using var scope =
                application.Services.CreateAsyncScope();
            var roles = scope.ServiceProvider.GetRequiredService<
                IIdentityRoleService<IntegrationProfile>>();
            var created = await roles.CreateAsync(
                new CreateRoleCommand(name),
                CancellationToken.None);
            Assert.True(created.IsSuccess);
            return created.Value;
        }

        public async Task<bool> IsUserInRoleAsync(
            Guid userId,
            Guid roleId)
        {
            await using var scope =
                application.Services.CreateAsyncScope();
            var roles = scope.ServiceProvider.GetRequiredService<
                IIdentityRoleService<IntegrationProfile>>();
            var result = await roles.IsUserInRoleAsync(
                userId,
                roleId,
                CancellationToken.None);
            Assert.True(result.IsSuccess);
            return result.Value;
        }

        public async Task CreateRolesAsync(IEnumerable<string> names)
        {
            await using var scope =
                application.Services.CreateAsyncScope();
            var roles = scope.ServiceProvider.GetRequiredService<
                IIdentityRoleService<IntegrationProfile>>();
            foreach (var name in names)
            {
                var created = await roles.CreateAsync(
                    new CreateRoleCommand(name),
                    CancellationToken.None);
                Assert.True(created.IsSuccess);
            }
        }

        public HttpClient CreateClient(
            bool allowAutoRedirect = true)
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
                AllowAutoRedirect = allowAutoRedirect,
            };

            return new HttpClient(handler)
            {
                BaseAddress = new Uri(address),
                Timeout = TimeSpan.FromSeconds(30),
            };
        }

        public async Task<string[]> GetRateLimitVersionsAsync(
            string scope)
        {
            await using var serviceScope =
                application.Services.CreateAsyncScope();
            var database = serviceScope.ServiceProvider
                .GetRequiredService<
                    PostgreSqlIdentityDbContext<
                        IntegrationProfile>>();
            return await database.RateLimitBuckets
                .AsNoTracking()
                .Where(bucket => bucket.Scope == scope)
                .Select(bucket => bucket.PartitionVersion)
                .Distinct()
                .OrderBy(version => version)
                .ToArrayAsync();
        }

        public async Task UpdateProfileAsync(
            Guid userId,
            IntegrationProfile profile)
        {
            await using var serviceScope =
                application.Services.CreateAsyncScope();
            var store = serviceScope.ServiceProvider
                .GetRequiredService<
                    IIdentityUserStore<IntegrationProfile>>();
            var users = serviceScope.ServiceProvider
                .GetRequiredService<
                    IIdentityUserService<IntegrationProfile>>();
            var current = await store.FindByIdAsync(
                userId,
                CancellationToken.None);
            Assert.NotNull(current);

            var updated = await users.PatchProfileAsync(
                new PatchProfileCommand<IntegrationProfile>(
                    userId,
                    current.Version,
                    profile),
                CancellationToken.None);

            Assert.True(updated.IsSuccess);
            Assert.Equal(current.Version + 1, updated.Value.Version);
            Assert.Equal(
                current.SecurityStamp,
                updated.Value.SecurityStamp);
        }

        public async Task<string> GetVerificationVerifierAsync(
            Guid challengeId)
        {
            await using var serviceScope =
                application.Services.CreateAsyncScope();
            var database = serviceScope.ServiceProvider
                .GetRequiredService<
                    PostgreSqlIdentityDbContext<
                        IntegrationProfile>>();
            return await database.VerificationChallenges
                .AsNoTracking()
                .Where(challenge =>
                    challenge.Id == challengeId)
                .Select(challenge => challenge.Verifier)
                .SingleAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await application.DisposeAsync();
        }
    }
}
