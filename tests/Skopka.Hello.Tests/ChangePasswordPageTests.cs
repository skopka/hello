using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.UI;
using Skopka.Hello.UI.Pages;
using Skopka.Identity.Errors;

namespace Skopka.Hello.Tests;

public sealed class ChangePasswordPageTests
{
    [Fact]
    public async Task TerminalFailureClearsChallengeAndShowsRestartState()
    {
        var challengeId = Guid.NewGuid();
        var application = new FakeApplication(
            OperationResultFactory.Fail(
                [
                    new Error(
                        HelloPasswordChangeErrorCodes.RestartRequired,
                        "Request a new code.",
                        ErrorType.Conflict),
                    new Error(
                        IdentityErrorCodes.PasswordRejected,
                        "The new password does not satisfy policy.",
                        ErrorType.Validation,
                        new ValidationDetails(
                            new Dictionary<string, string[]>
                            {
                                ["newPassword"] =
                                ["Use a stronger password."],
                            })),
                ]));
        var model = new ChangePasswordModel(
            application,
            new FakeSessionCookieManager())
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext(),
            },
            Input = new ChangePasswordModel.InputModel
            {
                ChallengeId = challengeId,
                VerificationCode = "123456",
                CurrentPassword = "current password",
                NewPassword = "new password",
                ConfirmPassword = "new password",
            },
        };

        var action = await model.OnPostChangeAsync(
            CancellationToken.None);

        Assert.IsType<PageResult>(action);
        Assert.True(model.RestartRequired);
        Assert.False(model.CodeRequested);
        Assert.Equal(Guid.Empty, model.Input.ChallengeId);
        Assert.Empty(model.Input.VerificationCode);
        var messages = model.ModelState[string.Empty]!.Errors
            .Select(error => error.ErrorMessage)
            .ToArray();
        Assert.Contains("Request a new code.", messages);
        Assert.Contains(
            "The new password does not satisfy policy.",
            messages);
        Assert.Equal(challengeId, application.LastCommand?.ChallengeId);
    }

    [Fact]
    public async Task CleanupFailureSignsOutAfterPasswordWasChanged()
    {
        var authentication = new FakeAuthenticationService();
        await using var services = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(authentication)
            .BuildServiceProvider();
        var application = new FakeApplication(
            OperationResultFactory.Fail(
                new Error(
                    HelloPasswordChangeErrorCodes
                        .SessionCleanupRequired,
                    "Sign in again with the new password.",
                    ErrorType.Conflict)));
        var cookies = new FakeSessionCookieManager();
        var model = new ChangePasswordModel(application, cookies)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = services,
                },
            },
            Input = new ChangePasswordModel.InputModel
            {
                ChallengeId = Guid.NewGuid(),
                VerificationCode = "123456",
                CurrentPassword = "current password",
                NewPassword = "new password",
                ConfirmPassword = "new password",
            },
        };

        var action = await model.OnPostChangeAsync(
            CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(action);
        Assert.Equal("/SkopkaHello/Login", redirect.PageName);
        Assert.Equal(
            true,
            redirect.RouteValues?["passwordChangedSessionCleanup"]);
        Assert.True(cookies.SessionCookiesDeleted);
        Assert.Equal(
            HelloUiDefaults.AuthenticationScheme,
            authentication.SignedOutScheme);
    }

    private sealed class FakeApplication(OperationResult result)
        : IHelloUiApplication
    {
        public HelloUiCompletePasswordChangeCommand? LastCommand
        {
            get;
            private set;
        }

        public Task<OperationResult> CompletePasswordChangeAsync(
            HelloUiCompletePasswordChangeCommand command,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            LastCommand = command;
            return Task.FromResult(result);
        }

        public Task<OperationResult> RegisterAsync(
            HelloUiRegisterCommand command,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<OperationResult<HelloUiSignIn>> LoginAsync(
            HelloUiLoginCommand command,
            HttpContext httpContext,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<OperationResult> RequestPasswordResetAsync(
            string email,
            HttpContext httpContext,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<OperationResult> ResetPasswordAsync(
            HelloUiResetPasswordCommand command,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<OperationResult> RequestEmailConfirmationAsync(
            string email,
            HttpContext httpContext,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<OperationResult> ConfirmEmailAsync(
            HelloUiConfirmEmailCommand command,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<OperationResult> RequestPhoneConfirmationAsync(
            string phone,
            HttpContext httpContext,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<OperationResult> ConfirmPhoneAsync(
            HelloUiConfirmPhoneCommand command,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<OperationResult<IReadOnlyList<HelloSessionInfo>>>
            ListSessionsAsync(
                Guid userId,
                CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<OperationResult> RevokeSessionAsync(
            Guid userId,
            Guid sessionId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<OperationResult> LogoutAsync(
            string refreshToken,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<OperationResult> LogoutAllAsync(
            Guid userId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<OperationResult<HelloStepUpChallenge>>
            BeginPasswordChangeAsync(
                HttpContext httpContext,
                CancellationToken cancellationToken)
            => throw new NotSupportedException();
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
        {
            SessionCookiesDeleted = true;
        }
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
