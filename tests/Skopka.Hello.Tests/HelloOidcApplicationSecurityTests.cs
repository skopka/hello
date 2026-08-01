using System.Security.Claims;
using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.Oidc;
using Skopka.Identity.ExternalLogins;
using Skopka.Identity.Sessions;
using Skopka.Identity.SignInMethods;

namespace Skopka.Hello.Tests;

public sealed class HelloOidcApplicationSecurityTests
{
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
                    DateTimeOffset.UtcNow.AddMinutes(5))),
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
        Assert.Equal(
            external.Snapshot!.Version.ToString(
                CultureInfo.InvariantCulture),
            written.Properties.Items[
                "hello:oidc:expected_version"]);
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
                expectedVersion: 7,
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
            expectedVersion: 7,
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
        long? expectedVersion = null,
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

        if (expectedVersion is not null)
        {
            properties.Items["hello:oidc:expected_version"] =
                expectedVersion.Value.ToString(
                    CultureInfo.InvariantCulture);
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
            bool passwordSignInEnabled = true)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
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

    private sealed record SignInCall(
        string Scheme,
        ClaimsPrincipal Principal,
        AuthenticationProperties Properties);

    private sealed class FakeExternalIdentityApplication
        : IHelloExternalIdentityApplication<TestProfile>
    {
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

        public Task<OperationResult<HelloSignIn<TestProfile>>> SignInAsync(
            HelloExternalSignInCommand command,
            CancellationToken cancellationToken)
            => Unexpected<HelloSignIn<TestProfile>>();

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
