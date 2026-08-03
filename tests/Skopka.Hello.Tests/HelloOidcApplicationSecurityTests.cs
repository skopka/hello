using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.Oidc;
using Skopka.Identity.Errors;
using Skopka.Identity.ExternalLogins;
using Skopka.Identity.Sessions;
using Skopka.Identity.SignInMethods;

namespace Skopka.Hello.Tests;

public sealed class HelloOidcApplicationSecurityTests
{
    [Fact]
    public async Task DisabledRegistrationRejectsUnknownExternalIdentity()
    {
        var authentication = new FakeAuthenticationService();
        var ticket = CreateTicket(
            HelloOidcDefaults.ExternalCookieScheme,
            intent: "sign_in");
        ticket.Properties.ExpiresUtc =
            DateTimeOffset.UtcNow.AddMinutes(3);
        authentication.SetTicket(
            HelloOidcDefaults.ExternalCookieScheme,
            ticket);
        var external = new FakeExternalIdentityApplication
        {
            SignInResult = OperationResultFactory.Fail<
                HelloSignIn<TestProfile>>(
                    new Error(
                        IdentityErrorCodes.ExternalLoginNotFound,
                        "External login not found.",
                        ErrorType.NotFound)),
        };
        using var fixture = new Fixture(
            authentication,
            external,
            selfRegistrationEnabled: false);

        var result = await fixture.Application.CompleteChallengeAsync(
            fixture.HttpContext,
            null,
            new IdentitySessionMetadata("Browser", "Device"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Errors,
            error => error.Code
                == HelloRegistrationErrors.DisabledCode);
        Assert.Equal(1, external.TotalCalls);
        Assert.Contains(
            HelloOidcDefaults.ExternalCookieScheme,
            authentication.SignOuts);
        Assert.DoesNotContain(
            authentication.SignIns,
            call => call.Scheme
                == HelloOidcDefaults.PendingCookieScheme);
    }

    [Fact]
    public async Task DisabledRegistrationStillAllowsLinkedExternalSignIn()
    {
        var authentication = new FakeAuthenticationService();
        var ticket = CreateTicket(
            HelloOidcDefaults.ExternalCookieScheme,
            intent: "sign_in");
        ticket.Properties.ExpiresUtc =
            DateTimeOffset.UtcNow.AddMinutes(3);
        authentication.SetTicket(
            HelloOidcDefaults.ExternalCookieScheme,
            ticket);
        var now = DateTimeOffset.UtcNow;
        var signIn = new HelloSignIn<TestProfile>(
            new HelloAccount<TestProfile>(
                Guid.NewGuid(),
                Skopka.Identity.Users.UserFlags.None,
                "alice",
                "alice@example.test",
                true,
                null,
                false,
                new TestProfile(),
                1,
                now,
                now),
            new HelloSession(
                Guid.NewGuid(),
                "access-token",
                now.AddMinutes(5),
                "refresh-token",
                now.AddDays(1)));
        var external = new FakeExternalIdentityApplication
        {
            SignInResult = OperationResultFactory.Success(signIn),
        };
        using var fixture = new Fixture(
            authentication,
            external,
            selfRegistrationEnabled: false);

        var result = await fixture.Application.CompleteChallengeAsync(
            fixture.HttpContext,
            null,
            new IdentitySessionMetadata("Browser", "Device"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            HelloOidcCompletionKind.SignedIn,
            result.Value.Kind);
        Assert.Same(signIn, result.Value.SignIn);
        Assert.Equal(1, external.TotalCalls);
        Assert.Contains(
            HelloOidcDefaults.ExternalCookieScheme,
            authentication.SignOuts);
        Assert.DoesNotContain(
            authentication.SignIns,
            call => call.Scheme
                == HelloOidcDefaults.PendingCookieScheme);
    }

    [Fact]
    public async Task DisabledRegistrationClearsStalePendingTicket()
    {
        var authentication = new FakeAuthenticationService();
        authentication.SetTicket(
            HelloOidcDefaults.PendingCookieScheme,
            CreateTicket(
                HelloOidcDefaults.PendingCookieScheme,
                intent: "sign_in"));
        var external = new FakeExternalIdentityApplication();
        using var fixture = new Fixture(
            authentication,
            external,
            selfRegistrationEnabled: false);

        var result = await fixture.Application.GetRegistrationHintsAsync(
            fixture.HttpContext,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Errors,
            error => error.Code
                == HelloRegistrationErrors.DisabledCode);
        Assert.Equal(0, external.TotalCalls);
        Assert.Contains(
            HelloOidcDefaults.PendingCookieScheme,
            authentication.SignOuts);
    }

    [Fact]
    public async Task DisabledRegistrationClearsPendingTicketOnRegister()
    {
        var authentication = new FakeAuthenticationService();
        authentication.SetTicket(
            HelloOidcDefaults.PendingCookieScheme,
            CreateTicket(
                HelloOidcDefaults.PendingCookieScheme,
                intent: "sign_in"));
        var external = new FakeExternalIdentityApplication();
        using var fixture = new Fixture(
            authentication,
            external,
            selfRegistrationEnabled: false);

        var result = await fixture.Application.RegisterAsync(
            new HelloOidcRegisterCommand<TestProfile>(
                "alice",
                "alice@example.test",
                null,
                new TestProfile(),
                new IdentitySessionMetadata("Browser", "Device")),
            fixture.HttpContext,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Equal(
            HelloRegistrationErrors.DisabledCode,
            error.Code);
        Assert.Equal(ErrorType.Forbidden, error.Type);
        Assert.Equal(0, external.TotalCalls);
        Assert.Contains(
            HelloOidcDefaults.PendingCookieScheme,
            authentication.SignOuts);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("false", null)]
    [InlineData("not-a-boolean", null)]
    [InlineData("true", "alice@example.test")]
    public async Task RegistrationHintsUseOnlyVerifiedEmail(
        string? emailVerified,
        string? expectedEmail)
    {
        var authentication = new FakeAuthenticationService();
        authentication.SetTicket(
            HelloOidcDefaults.PendingCookieScheme,
            CreateTicket(
                HelloOidcDefaults.PendingCookieScheme,
                intent: "sign_in",
                email: "alice@example.test",
                emailVerified: emailVerified));
        var external = new FakeExternalIdentityApplication();
        using var fixture = new Fixture(authentication, external);

        var result = await fixture.Application
            .GetRegistrationHintsAsync(
                fixture.HttpContext,
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedEmail, result.Value.VerifiedEmail);
        Assert.Equal("Alice", result.Value.DisplayName);
        Assert.Equal("en", result.Value.Locale);
        Assert.Equal("/hello/account", result.Value.ReturnUrl);
        Assert.Equal(0, external.TotalCalls);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("excessive_lifetime")]
    [InlineData("missing_expiry")]
    [InlineData("missing_flow_id")]
    [InlineData("missing_subject")]
    [InlineData("provider_mismatch")]
    public async Task MalformedOrExpiredPendingTicketIsRejected(
        string mutation)
    {
        var ticket = CreateTicket(
            HelloOidcDefaults.PendingCookieScheme,
            intent: "sign_in",
            email: "alice@example.test",
            emailVerified: "true");
        MutateTicket(ticket, mutation);
        var authentication = new FakeAuthenticationService();
        authentication.SetTicket(
            HelloOidcDefaults.PendingCookieScheme,
            ticket);
        var external = new FakeExternalIdentityApplication();
        using var fixture = new Fixture(authentication, external);

        var result = await fixture.Application
            .GetRegistrationHintsAsync(
                fixture.HttpContext,
                CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Errors,
            error => error.Code
                == "hello.oidc.pending_identity_invalid");
        Assert.Equal(0, external.TotalCalls);
    }

    [Fact]
    public async Task BeginUnlinkIgnoresDisabledAlternateProvider()
    {
        var userId = Guid.NewGuid();
        var external = new FakeExternalIdentityApplication
        {
            Snapshot = CreateSnapshot(
                userId,
                hasPassword: false,
                "github",
                "legacy-disabled"),
        };
        using var fixture = new Fixture(
            new FakeAuthenticationService(),
            external);

        var result = await fixture.Application.BeginUnlinkAsync(
            "github",
            fixture.HttpContext,
            CreateLocalSession(userId),
            clientKey: null,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Errors,
            error => error.Code
                == "hello.oidc.last_sign_in_method");
        Assert.Equal(0, external.BeginUnlinkCalls);
    }

    [Fact]
    public async Task BeginUnlinkIgnoresPasswordWhenPasswordSignInIsDisabled()
    {
        var userId = Guid.NewGuid();
        var external = new FakeExternalIdentityApplication
        {
            Snapshot = CreateSnapshot(
                userId,
                hasPassword: true,
                "github"),
        };
        using var fixture = new Fixture(
            new FakeAuthenticationService(),
            external,
            passwordSignInEnabled: false);

        var result = await fixture.Application.BeginUnlinkAsync(
            "github",
            fixture.HttpContext,
            CreateLocalSession(userId),
            clientKey: null,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Errors,
            error => error.Code
                == "hello.oidc.last_sign_in_method");
        Assert.Equal(0, external.BeginUnlinkCalls);
    }

    [Fact]
    public async Task BeginUnlinkWritesUserBoundPendingTicketWhenAlternateIsEnabled()
    {
        var userId = Guid.NewGuid();
        var localSession = CreateLocalSession(userId);
        var challengeId = Guid.NewGuid();
        var authentication = new FakeAuthenticationService();
        var external = new FakeExternalIdentityApplication
        {
            Snapshot = CreateSnapshot(
                userId,
                hasPassword: false,
                "github",
                "contoso"),
            BeginUnlinkResult = OperationResultFactory.Success(
                new HelloStepUpChallenge(
                    challengeId,
                    DateTimeOffset.UtcNow.AddMinutes(5),
                    HelloDeliveryChannel.Email)),
        };
        using var fixture = new Fixture(
            authentication,
            external,
            includeContoso: true);

        var result = await fixture.Application.BeginUnlinkAsync(
            "GITHUB",
            fixture.HttpContext,
            localSession,
            "client-partition",
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(challengeId, result.Value.ChallengeId);
        Assert.Equal(1, external.BeginUnlinkCalls);
        Assert.Equal(
            new ExternalLoginKey("github", "subject-github"),
            external.LastBeginUnlink?.Login);
        Assert.Equal(
            "client-partition",
            external.LastBeginUnlink?.ClientKey);

        var written = Assert.Single(authentication.SignIns);
        Assert.Equal(
            HelloOidcDefaults.PendingCookieScheme,
            written.Scheme);
        Assert.Equal(
            "unlink",
            written.Properties.Items["hello:oidc:intent"]);
        Assert.Equal(
            userId.ToString("D"),
            written.Properties.Items["hello:oidc:user_id"]);
        Assert.Equal(
            localSession.SessionId.ToString("D"),
            written.Properties.Items["hello:oidc:session_id"]);
        Assert.DoesNotContain(
            "hello:oidc:expected_version",
            written.Properties.Items.Keys);
        Assert.Equal(
            challengeId.ToString("D"),
            written.Properties.Items["hello:oidc:challenge_id"]);
        Assert.Equal(
            "subject-github",
            written.Principal.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task CompleteUnlinkRechecksLastMethodBeforeMutation()
    {
        var userId = Guid.NewGuid();
        var localSession = CreateLocalSession(userId);
        var authentication = new FakeAuthenticationService();
        authentication.SetTicket(
            HelloOidcDefaults.PendingCookieScheme,
            CreateTicket(
                HelloOidcDefaults.PendingCookieScheme,
                intent: "unlink",
                userId: userId,
                sessionId: localSession.SessionId,
                challengeId: Guid.NewGuid()));
        var external = new FakeExternalIdentityApplication
        {
            Snapshot = CreateSnapshot(
                userId,
                hasPassword: false,
                "github"),
        };
        using var fixture = new Fixture(authentication, external);

        var result = await fixture.Application.CompleteUnlinkAsync(
            "123456",
            fixture.HttpContext,
            localSession,
            new IdentitySessionMetadata("Browser", "Device"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Errors,
            error => error.Code
                == "hello.oidc.last_sign_in_method");
        Assert.Equal(0, external.CompleteUnlinkCalls);
        Assert.Contains(
            HelloOidcDefaults.PendingCookieScheme,
            authentication.SignOuts);
    }

    [Fact]
    public async Task CompletionUsesFreshSignInMethodSnapshotVersion()
    {
        var userId = Guid.NewGuid();
        var localSession = CreateLocalSession(userId);
        var authentication = new FakeAuthenticationService();
        var ticket = CreateTicket(
            HelloOidcDefaults.PendingCookieScheme,
            intent: "unlink",
            userId: userId,
            sessionId: localSession.SessionId,
            challengeId: Guid.NewGuid());
        ticket.Properties.Items["hello:oidc:expected_version"] = "7";
        authentication.SetTicket(
            HelloOidcDefaults.PendingCookieScheme,
            ticket);
        var external = new FakeExternalIdentityApplication
        {
            Snapshot = CreateSnapshot(
                userId,
                hasPassword: true,
                "github") with
            {
                Version = 8,
            },
            CompleteUnlinkResult = OperationResultFactory
                .Fail<HelloSignIn<TestProfile>>(
                    new Error(
                        "test.invalid_verification_code",
                        "The verification code is invalid.",
                        ErrorType.Validation)),
        };
        using var fixture = new Fixture(authentication, external);

        var result = await fixture.Application.CompleteUnlinkAsync(
            "wrong-code",
            fixture.HttpContext,
            localSession,
            new IdentitySessionMetadata("Browser", "Device"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(8, external.LastCompleteUnlink?.ExpectedVersion);
        Assert.Equal(
            new ExternalLoginKey("github", "subject-github"),
            external.LastCompleteUnlink?.Login);
    }

    [Fact]
    public async Task RetryableCompletionRotatesFlowAndRejectsOldReplay()
    {
        var userId = Guid.NewGuid();
        var localSession = CreateLocalSession(userId);
        var flowId = Guid.NewGuid();
        var authentication = new FakeAuthenticationService();
        var pendingTicket = CreateTicket(
            HelloOidcDefaults.PendingCookieScheme,
            intent: "unlink",
            userId: userId,
            sessionId: localSession.SessionId,
            challengeId: Guid.NewGuid(),
            flowId: flowId);
        authentication.SetTicket(
            HelloOidcDefaults.PendingCookieScheme,
            pendingTicket);
        var external = new FakeExternalIdentityApplication
        {
            Snapshot = CreateSnapshot(
                userId,
                hasPassword: true,
                "github"),
            CompleteUnlinkResult = OperationResultFactory
                .Fail<HelloSignIn<TestProfile>>(
                    new Error(
                        "test.invalid_verification_code",
                        "The verification code is invalid.",
                        ErrorType.Validation)),
        };
        using var fixture = new Fixture(authentication, external);

        var first = await fixture.Application.CompleteUnlinkAsync(
            "wrong-code",
            fixture.HttpContext,
            localSession,
            new IdentitySessionMetadata("Browser", "Device"),
            CancellationToken.None);

        Assert.False(first.IsSuccess);
        var rotated = Assert.Single(authentication.SignIns);
        Assert.True(Guid.TryParse(
            rotated.Properties.Items["hello:oidc:flow_id"],
            out var rotatedFlowId));
        Assert.NotEqual(flowId, rotatedFlowId);
        Assert.Equal(
            pendingTicket.Properties.ExpiresUtc,
            rotated.Properties.ExpiresUtc);
        var callsAfterFirstAttempt = external.TotalCalls;

        var replay = await fixture.Application.CompleteUnlinkAsync(
            "wrong-code",
            fixture.HttpContext,
            localSession,
            new IdentitySessionMetadata("Browser", "Device"),
            CancellationToken.None);

        Assert.False(replay.IsSuccess);
        Assert.Contains(
            replay.Errors,
            error => error.Code
                == "hello.oidc.pending_identity_invalid");
        Assert.Equal(callsAfterFirstAttempt, external.TotalCalls);
        Assert.Contains(
            HelloOidcDefaults.PendingCookieScheme,
            authentication.SignOuts);
    }

    [Fact]
    public async Task PreVerificationConcurrencyRotatesFlowAndKeepsChallengeRetryable()
    {
        var userId = Guid.NewGuid();
        var localSession = CreateLocalSession(userId);
        var flowId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var authentication = new FakeAuthenticationService();
        var pendingTicket = CreateTicket(
            HelloOidcDefaults.PendingCookieScheme,
            intent: "unlink",
            userId: userId,
            sessionId: localSession.SessionId,
            challengeId: challengeId,
            flowId: flowId);
        authentication.SetTicket(
            HelloOidcDefaults.PendingCookieScheme,
            pendingTicket);
        var external = new FakeExternalIdentityApplication
        {
            Snapshot = CreateSnapshot(
                userId,
                hasPassword: true,
                "github"),
            CompleteUnlinkResult = OperationResultFactory
                .Fail<HelloSignIn<TestProfile>>(
                    new Error(
                        IdentityErrorCodes.ConcurrencyConflict,
                        "Concurrency conflict.",
                        ErrorType.Conflict)),
        };
        using var fixture = new Fixture(authentication, external);

        var first = await fixture.Application.CompleteUnlinkAsync(
            "123456",
            fixture.HttpContext,
            localSession,
            new IdentitySessionMetadata("Browser", "Device"),
            CancellationToken.None);

        Assert.False(first.IsSuccess);
        Assert.Equal(
            IdentityErrorCodes.ConcurrencyConflict,
            Assert.Single(first.Errors).Code);
        var rotated = Assert.Single(authentication.SignIns);
        Assert.True(Guid.TryParse(
            rotated.Properties.Items["hello:oidc:flow_id"],
            out var rotatedFlowId));
        Assert.NotEqual(flowId, rotatedFlowId);
        Assert.Equal(
            challengeId.ToString("D"),
            rotated.Properties.Items["hello:oidc:challenge_id"]);
        Assert.DoesNotContain(
            HelloOidcDefaults.PendingCookieScheme,
            authentication.SignOuts);

        authentication.SetTicket(
            HelloOidcDefaults.PendingCookieScheme,
            new AuthenticationTicket(
                rotated.Principal,
                rotated.Properties,
                rotated.Scheme));
        var retry = await fixture.Application.CompleteUnlinkAsync(
            "123456",
            fixture.HttpContext,
            localSession,
            new IdentitySessionMetadata("Browser", "Device"),
            CancellationToken.None);

        Assert.False(retry.IsSuccess);
        Assert.Equal(2, external.CompleteUnlinkCalls);
        Assert.Equal(challengeId, external.LastCompleteUnlink?.ChallengeId);
        Assert.Equal(2, authentication.SignIns.Count);
        Assert.DoesNotContain(
            HelloOidcDefaults.PendingCookieScheme,
            authentication.SignOuts);
    }

    [Theory]
    [InlineData(
        HelloExternalIdentityErrorCodes.ChallengeRestartRequired)]
    [InlineData(HelloExternalIdentityErrorCodes.RestartRequired)]
    public async Task TerminalCompletionFailureDeletesPendingFlow(
        string terminalErrorCode)
    {
        var userId = Guid.NewGuid();
        var localSession = CreateLocalSession(userId);
        var authentication = new FakeAuthenticationService();
        authentication.SetTicket(
            HelloOidcDefaults.PendingCookieScheme,
            CreateTicket(
                HelloOidcDefaults.PendingCookieScheme,
                intent: "unlink",
                userId: userId,
                sessionId: localSession.SessionId,
                challengeId: Guid.NewGuid()));
        var external = new FakeExternalIdentityApplication
        {
            Snapshot = CreateSnapshot(
                userId,
                hasPassword: true,
                "github"),
            CompleteUnlinkResult = OperationResultFactory
                .Fail<HelloSignIn<TestProfile>>(
                    [
                        new Error(
                            terminalErrorCode,
                            "Restart required.",
                            ErrorType.Conflict),
                        new Error(
                            IdentityErrorCodes
                                .VerificationChallengeInvalid,
                            "Challenge invalid.",
                            ErrorType.Validation),
                    ]),
        };
        using var fixture = new Fixture(authentication, external);

        var result = await fixture.Application.CompleteUnlinkAsync(
            "123456",
            fixture.HttpContext,
            localSession,
            new IdentitySessionMetadata("Browser", "Device"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(authentication.SignIns);
        Assert.Contains(
            HelloOidcDefaults.PendingCookieScheme,
            authentication.SignOuts);
    }

    private static HelloOidcLocalSession CreateLocalSession(Guid userId)
        => new(userId, Guid.NewGuid(), "old-access-token");

    private static SignInMethodSnapshot CreateSnapshot(
        Guid userId,
        bool hasPassword,
        params string[] providers)
        => new(
            userId,
            7,
            hasPassword,
            providers.Select((provider, index) =>
                new ExternalLoginInfo(
                    userId,
                    new ExternalLoginKey(
                        provider,
                        $"subject-{provider}"),
                    DateTimeOffset.UtcNow.AddMinutes(-index - 1)))
                .ToArray());

    private static AuthenticationTicket CreateTicket(
        string scheme,
        string intent,
        string? email = null,
        string? emailVerified = null,
        Guid? userId = null,
        Guid? sessionId = null,
        Guid? challengeId = null,
        Guid? flowId = null)
    {
        var identity = new ClaimsIdentity(scheme);
        identity.AddClaim(
            new Claim("hello:oidc:provider", "github"));
        identity.AddClaim(new Claim("sub", "subject-github"));
        identity.AddClaim(new Claim("name", "Alice"));
        identity.AddClaim(new Claim("locale", "en"));
        if (email is not null)
        {
            identity.AddClaim(new Claim("email", email));
        }

        if (emailVerified is not null)
        {
            identity.AddClaim(
                new Claim("email_verified", emailVerified));
        }

        var now = DateTimeOffset.UtcNow;
        var properties = new AuthenticationProperties
        {
            IssuedUtc = now.AddMinutes(-1),
            ExpiresUtc = now.AddMinutes(5),
        };
        properties.Items["hello:oidc:intent"] = intent;
        properties.Items["hello:oidc:provider"] = "github";
        properties.Items["hello:oidc:return_url"] =
            "/hello/account";
        properties.Items["hello:oidc:flow_id"] =
            (flowId ?? Guid.NewGuid()).ToString("D");
        if (userId is not null)
        {
            properties.Items["hello:oidc:user_id"] =
                userId.Value.ToString("D");
        }

        if (sessionId is not null)
        {
            properties.Items["hello:oidc:session_id"] =
                sessionId.Value.ToString("D");
        }

        if (challengeId is not null)
        {
            properties.Items["hello:oidc:challenge_id"] =
                challengeId.Value.ToString("D");
        }

        return new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            properties,
            scheme);
    }

    private static void MutateTicket(
        AuthenticationTicket ticket,
        string mutation)
    {
        var now = DateTimeOffset.UtcNow;
        switch (mutation)
        {
            case "expired":
                ticket.Properties.IssuedUtc = now.AddMinutes(-2);
                ticket.Properties.ExpiresUtc = now.AddMinutes(-1);
                break;
            case "excessive_lifetime":
                ticket.Properties.IssuedUtc = now;
                ticket.Properties.ExpiresUtc = now.AddMinutes(11);
                break;
            case "missing_expiry":
                ticket.Properties.ExpiresUtc = null;
                break;
            case "missing_subject":
                var identity = (ClaimsIdentity)ticket.Principal.Identity!;
                identity.RemoveClaim(identity.FindFirst("sub")!);
                break;
            case "missing_flow_id":
                ticket.Properties.Items.Remove("hello:oidc:flow_id");
                break;
            case "provider_mismatch":
                ticket.Properties.Items["hello:oidc:provider"] =
                    "contoso";
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(mutation),
                    mutation,
                    "Unknown ticket mutation.");
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider root;
        private readonly IServiceScope scope;

        public Fixture(
            FakeAuthenticationService authentication,
            FakeExternalIdentityApplication external,
            bool includeContoso = false,
            bool passwordSignInEnabled = true,
            bool selfRegistrationEnabled = true)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            services.AddSkopkaHello<TestProfile>(options =>
                options.SelfRegistrationEnabled =
                    selfRegistrationEnabled);
            services.AddSingleton<IHelloOidcFlowStore,
                AllowingFlowStore>();
            services.AddSingleton<
                IHelloExternalIdentityApplication<TestProfile>>(external);
            services.AddSkopkaHelloOidc<TestProfile>(options =>
            {
                options.PublicOrigin = new Uri(
                    "https://hello.example.test");
                options.PasswordSignInEnabled =
                    passwordSignInEnabled;
                options.Providers["github"] = CreateProvider(
                    "GitHub",
                    "https://github.example.test");
                if (includeContoso)
                {
                    options.Providers["contoso"] = CreateProvider(
                        "Contoso",
                        "https://contoso.example.test");
                }
            });
            services.RemoveAll<IAuthenticationService>();
            services.AddSingleton<IAuthenticationService>(
                authentication);
            root = services.BuildServiceProvider();
            scope = root.CreateScope();
            HttpContext = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider,
            };
            Application = scope.ServiceProvider.GetRequiredService<
                IHelloOidcApplication<TestProfile>>();
        }

        public DefaultHttpContext HttpContext { get; }

        public IHelloOidcApplication<TestProfile> Application { get; }

        public void Dispose()
        {
            scope.Dispose();
            root.Dispose();
        }

        private static HelloOidcProviderOptions CreateProvider(
            string displayName,
            string authority)
            => new()
            {
                DisplayName = displayName,
                Authority = authority,
                ClientId = "hello-tests",
                ClientSecret = "not-a-production-secret",
            };
    }

    private sealed class FakeAuthenticationService
        : IAuthenticationService
    {
        private readonly Dictionary<string, AuthenticateResult> results =
            new(StringComparer.Ordinal);

        public List<SignInCall> SignIns { get; } = [];

        public List<string> SignOuts { get; } = [];

        public void SetTicket(string scheme, AuthenticationTicket ticket)
            => results[scheme] = AuthenticateResult.Success(ticket);

        public Task<AuthenticateResult> AuthenticateAsync(
            HttpContext context,
            string? scheme)
            => Task.FromResult(
                scheme is not null
                && results.TryGetValue(scheme, out var result)
                    ? result
                    : AuthenticateResult.NoResult());

        public Task ChallengeAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
            => throw new NotSupportedException();

        public Task ForbidAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
            => throw new NotSupportedException();

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties)
        {
            SignIns.Add(
                new SignInCall(
                    scheme ?? string.Empty,
                    principal,
                    properties ?? new AuthenticationProperties()));
            return Task.CompletedTask;
        }

        public Task SignOutAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
        {
            SignOuts.Add(scheme ?? string.Empty);
            return Task.CompletedTask;
        }
    }

    private sealed class AllowingFlowStore : IHelloOidcFlowStore
    {
        private readonly HashSet<Guid> consumed = [];

        public Task<bool> TryConsumeAsync(
            Guid flowId,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                flowId != Guid.Empty
                && expiresAt > DateTimeOffset.UtcNow
                && consumed.Add(flowId));
        }
    }

    private sealed record SignInCall(
        string Scheme,
        ClaimsPrincipal Principal,
        AuthenticationProperties Properties);

    private sealed class FakeExternalIdentityApplication
        : IHelloExternalIdentityApplication<TestProfile>
    {
        public OperationResult<HelloSignIn<TestProfile>>? SignInResult
        {
            get;
            init;
        }

        public SignInMethodSnapshot? Snapshot { get; init; }

        public OperationResult<HelloStepUpChallenge>? BeginUnlinkResult
        {
            get;
            init;
        }

        public OperationResult<HelloSignIn<TestProfile>>?
            CompleteUnlinkResult
        {
            get;
            init;
        }

        public int BeginUnlinkCalls { get; private set; }

        public int CompleteUnlinkCalls { get; private set; }

        public int TotalCalls { get; private set; }

        public HelloBeginExternalLoginMutationCommand? LastBeginUnlink
        {
            get;
            private set;
        }

        public HelloCompleteExternalLoginMutationCommand?
            LastCompleteUnlink
        {
            get;
            private set;
        }

        public Task<OperationResult<HelloSignIn<TestProfile>>> SignInAsync(
            HelloExternalSignInCommand command,
            CancellationToken cancellationToken)
        {
            TotalCalls++;
            return Task.FromResult(
                SignInResult
                ?? throw new InvalidOperationException(
                    "SignInAsync was not expected."));
        }

        public Task<OperationResult<HelloSignIn<TestProfile>>> RegisterAsync(
            HelloExternalRegistrationCommand<TestProfile> command,
            CancellationToken cancellationToken)
            => Unexpected<HelloSignIn<TestProfile>>();

        public Task<OperationResult<SignInMethodSnapshot>>
            GetSignInMethodsAsync(
                string accessToken,
                CancellationToken cancellationToken)
        {
            TotalCalls++;
            return Task.FromResult(
                OperationResultFactory.Success(
                    Snapshot
                    ?? throw new InvalidOperationException(
                        "A sign-in method snapshot was not configured.")));
        }

        public Task<OperationResult<HelloStepUpChallenge>> BeginLinkAsync(
            HelloBeginExternalLoginMutationCommand command,
            CancellationToken cancellationToken)
            => Unexpected<HelloStepUpChallenge>();

        public Task<OperationResult<HelloSignIn<TestProfile>>>
            CompleteLinkAsync(
                HelloCompleteExternalLoginMutationCommand command,
                CancellationToken cancellationToken)
            => Unexpected<HelloSignIn<TestProfile>>();

        public Task<OperationResult<HelloStepUpChallenge>> BeginUnlinkAsync(
            HelloBeginExternalLoginMutationCommand command,
            CancellationToken cancellationToken)
        {
            TotalCalls++;
            BeginUnlinkCalls++;
            LastBeginUnlink = command;
            return Task.FromResult(
                BeginUnlinkResult
                ?? throw new InvalidOperationException(
                    "A begin-unlink result was not configured."));
        }

        public Task<OperationResult<HelloSignIn<TestProfile>>>
            CompleteUnlinkAsync(
                HelloCompleteExternalLoginMutationCommand command,
                CancellationToken cancellationToken)
        {
            TotalCalls++;
            CompleteUnlinkCalls++;
            LastCompleteUnlink = command;
            return Task.FromResult(
                CompleteUnlinkResult
                ?? throw new InvalidOperationException(
                    "CompleteUnlinkAsync was not expected."));
        }

        private Task<OperationResult<T>> Unexpected<T>()
        {
            TotalCalls++;
            throw new InvalidOperationException(
                "The external identity operation was not expected.");
        }
    }

    private sealed record TestProfile(string DisplayName = "Alice");
}
