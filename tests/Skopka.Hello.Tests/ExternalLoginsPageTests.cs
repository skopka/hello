using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.Oidc;
using Skopka.Hello.UI;
using Skopka.Hello.UI.Pages;

namespace Skopka.Hello.Tests;

public sealed class ExternalLoginsPageTests
{
    [Fact]
    public async Task TerminalMutationFailureClearsBrowserSession()
    {
        var authentication = new FakeAuthenticationService();
        await using var services = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(authentication)
            .BuildServiceProvider();
        var application = new FakeExternalApplication();
        var cookies = new FakeSessionCookieManager();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
        };
        var model = new ExternalLoginsModel(application, cookies)
        {
            PageContext = new PageContext
            {
                HttpContext = httpContext,
            },
            Input = new ExternalLoginsModel.CodeInput
            {
                VerificationCode = "123456",
            },
        };

        var action = await model.OnPostCompleteLinkAsync(
            CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(action);
        Assert.Equal(
            "/hello/login?accountChangeRestarted=true",
            redirect.Url);
        Assert.True(application.BrowserFlowCleared);
        Assert.True(cookies.SessionCookiesDeleted);
        Assert.Equal(
            HelloUiDefaults.AuthenticationScheme,
            authentication.SignedOutScheme);
    }

    [Fact]
    public async Task CompletionCancelClearsTemporaryFlow()
    {
        var application = new FakeExternalApplication();
        var model = new ExternalCompleteModel(
            application,
            new FakeSessionCookieManager())
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        var action = await model.OnPostCancelAsync(
            CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(action);
        Assert.Equal(HelloUiDefaults.LoginPath, redirect.Url);
        Assert.True(application.BrowserFlowCleared);
    }

    [Fact]
    public async Task RegistrationCancelClearsTemporaryFlow()
    {
        var application = new FakeExternalApplication();
        var model = new ExternalRegisterModel(
            application,
            new FakeSessionCookieManager())
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        var action = await model.OnPostCancelAsync(
            CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(action);
        Assert.Equal(HelloUiDefaults.LoginPath, redirect.Url);
        Assert.True(application.BrowserFlowCleared);
    }

    private sealed class FakeExternalApplication
        : IHelloUiExternalApplication
    {
        public bool BrowserFlowCleared { get; private set; }

        public bool IsConfigured => true;

        public IReadOnlyList<HelloOidcProvider> Providers => [];

        public OperationResult<HelloOidcChallenge> CreateSignInChallenge(
            string providerId,
            string? returnUrl)
            => throw new NotSupportedException();

        public OperationResult<HelloOidcChallenge> CreateLinkChallenge(
            string providerId,
            HttpContext httpContext)
            => throw new NotSupportedException();

        public Task<OperationResult<HelloUiExternalCompletion>>
            CompleteChallengeAsync(
                HttpContext httpContext,
                CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<OperationResult<HelloOidcRegistrationHints>>
            GetRegistrationHintsAsync(
                HttpContext httpContext,
                CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<OperationResult<HelloUiExternalRegistration>>
            RegisterAsync(
                HelloUiExternalRegisterCommand command,
                HttpContext httpContext,
                CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<OperationResult<IReadOnlyList<
            HelloOidcLinkedProvider>>> ListLinkedProvidersAsync(
                HttpContext httpContext,
                CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<OperationResult<HelloOidcProvider>>
            GetPendingLinkAsync(
                HttpContext httpContext,
                CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<OperationResult<HelloStepUpChallenge>> BeginLinkAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<OperationResult<HelloUiSignIn>> CompleteLinkAsync(
            string verificationCode,
            HttpContext httpContext,
            CancellationToken cancellationToken)
            => Task.FromResult(
                OperationResultFactory.Fail<HelloUiSignIn>(
                    new Error(
                        HelloExternalIdentityErrorCodes.RestartRequired,
                        "Restart required.",
                        ErrorType.Conflict)));

        public Task<OperationResult<HelloStepUpChallenge>> BeginUnlinkAsync(
            string providerId,
            HttpContext httpContext,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<OperationResult<HelloUiSignIn>> CompleteUnlinkAsync(
            string verificationCode,
            HttpContext httpContext,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task ClearBrowserFlowAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            BrowserFlowCleared = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSessionCookieManager
        : IHelloSessionCookieManager
    {
        public bool SessionCookiesDeleted { get; private set; }

        public OperationResult ValidateTransport(HttpContext httpContext)
            => OperationResultFactory.Success();

        public Task<OperationResult> ValidateAntiforgeryAsync(
            HttpContext httpContext)
            => Task.FromResult(OperationResultFactory.Success());

        public string? ReadRefreshToken(HttpContext httpContext) => null;

        public void WriteSessionCookies(
            HttpContext httpContext,
            HelloSession session)
            => throw new NotSupportedException();

        public void DeleteSessionCookies(HttpContext httpContext)
            => SessionCookiesDeleted = true;
    }

    private sealed class FakeAuthenticationService
        : IAuthenticationService
    {
        public string? SignedOutScheme { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(
            HttpContext context,
            string? scheme)
            => Task.FromResult(AuthenticateResult.NoResult());

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
            => throw new NotSupportedException();

        public Task SignOutAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
        {
            SignedOutScheme = scheme;
            return Task.CompletedTask;
        }
    }
}
