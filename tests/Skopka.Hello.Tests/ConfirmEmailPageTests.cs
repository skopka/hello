using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.UI;
using Skopka.Hello.UI.Pages;

namespace Skopka.Hello.Tests;

public sealed class ConfirmEmailPageTests
{
    [Fact]
    public void GetOnlyPreparesAutomaticPost()
    {
        var application = new RecordingApplication();
        var context = new DefaultHttpContext();
        var model = CreateModel(application, context);

        model.OnGet();

        Assert.True(model.LinkValid);
        Assert.True(model.AutoSubmit);
        Assert.False(model.Confirmed);
        Assert.Equal(0, application.ConfirmEmailCallCount);
        Assert.Contains(
            "no-store",
            context.Response.Headers.CacheControl.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "no-referrer",
            context.Response.Headers["Referrer-Policy"].ToString());
    }

    [Fact]
    public async Task PostConfirmsAndIsNeverScheduledAgain()
    {
        var application = new RecordingApplication();
        var model = CreateModel(
            application,
            new DefaultHttpContext());

        var result = await model.OnPostAsync(
            CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.True(model.LinkValid);
        Assert.True(model.Confirmed);
        Assert.False(model.AutoSubmit);
        Assert.Equal(1, application.ConfirmEmailCallCount);
        Assert.Equal(model.UserId, application.LastCommand?.UserId);
        Assert.Equal(model.Email, application.LastCommand?.Email);
        Assert.Equal(model.Token, application.LastCommand?.Token);
    }

    [Fact]
    public async Task FailedPostLeavesManualFallbackWithoutAutoRetry()
    {
        var application = new RecordingApplication
        {
            ConfirmationResult = OperationResultFactory.Fail(
                new Error(
                    "identity.email_confirmation.invalid",
                    "The email confirmation link is invalid.",
                    ErrorType.Validation)),
        };
        var model = CreateModel(
            application,
            new DefaultHttpContext());

        var result = await model.OnPostAsync(
            CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(model.Confirmed);
        Assert.False(model.AutoSubmit);
        Assert.Equal(1, application.ConfirmEmailCallCount);
        Assert.False(model.ModelState.IsValid);
    }

    private static ConfirmEmailModel CreateModel(
        RecordingApplication application,
        HttpContext context)
        => new(application, TestHelloUiLocalizer.Instance)
        {
            PageContext = new PageContext
            {
                HttpContext = context,
            },
            UserId = Guid.Parse(
                "9c1c1199-dc38-46ae-a831-5b3b56d4121e"),
            Email = "alice@example.test",
            Token = "email-confirmation-token",
        };

    private sealed class RecordingApplication : IHelloUiApplication
    {
        public int ConfirmEmailCallCount { get; private set; }

        public HelloUiConfirmEmailCommand? LastCommand { get; private set; }

        public OperationResult ConfirmationResult { get; init; } =
            OperationResultFactory.Success();

        public Task<OperationResult> ConfirmEmailAsync(
            HelloUiConfirmEmailCommand command,
            CancellationToken cancellationToken)
        {
            ConfirmEmailCallCount++;
            LastCommand = command;
            return Task.FromResult(ConfirmationResult);
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

        public Task<OperationResult> CompletePasswordChangeAsync(
            HelloUiCompletePasswordChangeCommand command,
            HttpContext httpContext,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
