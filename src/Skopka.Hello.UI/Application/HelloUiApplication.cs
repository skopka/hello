using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Authentication;
using Skopka.Identity.Errors;

namespace Skopka.Hello.UI;

internal sealed class HelloUiApplication<TProfile>(
    IHelloIdentityApplication<TProfile> application,
    IHelloUiProfileFactory<TProfile> profiles,
    IHelloRequestContext requestContext,
    SkopkaHelloOptions helloOptions)
    : IHelloUiApplication
{
    public async Task<OperationResult> RegisterAsync(
        HelloUiRegisterCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var profile = profiles.Create(
            new HelloUiRegistrationProfile(
                command.DisplayName,
                command.Locale));
        if (!profile.IsSuccess)
        {
            return OperationResultFactory.Fail(profile.Errors);
        }

        var result = await application.RegisterAsync(
            new HelloRegisterCommand<TProfile>(
                command.UserName,
                command.Email,
                command.Phone,
                profile.Value,
                command.Password),
            cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success()
            : OperationResultFactory.Fail(result.Errors);
    }

    public async Task<OperationResult<HelloUiSignIn>> LoginAsync(
        HelloUiLoginCommand command,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(httpContext);

        if (!TryParseHandle(command.Handle, out var handle))
        {
            return OperationResultFactory.Fail<HelloUiSignIn>(
                new Error(
                    IdentityErrorCodes.Validation,
                    "Validation failed.",
                    ErrorType.Validation,
                    new ValidationDetails(
                        new Dictionary<string, string[]>
                        {
                            [nameof(command.Handle)] =
                            [
                                "Select email or user name.",
                            ],
                        })));
        }

        var result = await application.LoginAsync(
            new HelloLoginCommand(
                handle,
                command.Login,
                command.Password,
                requestContext.CreateClientKey(httpContext),
                requestContext.CreateSessionMetadata(
                    httpContext,
                    helloOptions.ClientName)),
            cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success(
                new HelloUiSignIn(
                    HelloUiPrincipalFactory.Create(
                        result.Value.Account,
                        result.Value.Session.SessionId,
                        profiles),
                    result.Value.Session))
            : OperationResultFactory.Fail<HelloUiSignIn>(
                result.Errors);
    }

    public Task<OperationResult> RequestPasswordResetAsync(
        string email,
        CancellationToken cancellationToken)
        => application.RequestPasswordResetAsync(
            email,
            cancellationToken);

    public Task<OperationResult> ResetPasswordAsync(
        HelloUiResetPasswordCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return application.ResetPasswordAsync(
            new HelloResetPasswordCommand(
                command.UserId,
                command.Token,
                command.NewPassword),
            cancellationToken);
    }

    public Task<OperationResult> RequestEmailConfirmationAsync(
        string email,
        CancellationToken cancellationToken)
        => application.RequestEmailConfirmationAsync(
            email,
            cancellationToken);

    public async Task<OperationResult> ConfirmEmailAsync(
        HelloUiConfirmEmailCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await application.ConfirmEmailAsync(
            new HelloConfirmEmailCommand(
                command.UserId,
                command.Email,
                command.Token),
            cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success()
            : OperationResultFactory.Fail(result.Errors);
    }

    public Task<OperationResult<IReadOnlyList<HelloSessionInfo>>>
        ListSessionsAsync(
            Guid userId,
            CancellationToken cancellationToken)
        => application.ListSessionsAsync(
            userId,
            cancellationToken);

    public Task<OperationResult> RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken)
        => application.RevokeSessionAsync(
            userId,
            sessionId,
            cancellationToken);

    public Task<OperationResult> LogoutAsync(
        string refreshToken,
        CancellationToken cancellationToken)
        => application.LogoutAsync(
            refreshToken,
            cancellationToken);

    public Task<OperationResult> LogoutAllAsync(
        Guid userId,
        CancellationToken cancellationToken)
        => application.LogoutAllAsync(
            userId,
            cancellationToken);

    public async Task<OperationResult<HelloStepUpChallenge>>
        BeginPasswordChangeAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var accessToken = await ReadAccessTokenAsync(httpContext);
        if (accessToken is null)
        {
            return InvalidSession<HelloStepUpChallenge>();
        }

        return await application.BeginPasswordChangeAsync(
            new HelloBeginPasswordChangeCommand(
                accessToken,
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
    }

    public async Task<OperationResult> CompletePasswordChangeAsync(
        HelloUiCompletePasswordChangeCommand command,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(httpContext);

        var accessToken = await ReadAccessTokenAsync(httpContext);
        if (accessToken is null)
        {
            return InvalidSession();
        }

        return await application.CompletePasswordChangeAsync(
            new HelloCompletePasswordChangeCommand(
                accessToken,
                command.ChallengeId,
                command.VerificationCode,
                command.CurrentPassword,
                command.NewPassword),
            cancellationToken);
    }

    private static async Task<string?> ReadAccessTokenAsync(
        HttpContext httpContext)
    {
        var authentication = await httpContext.AuthenticateAsync(
            HelloUiDefaults.AuthenticationScheme);
        return authentication.Succeeded
            ? authentication.Properties?.GetTokenValue(
                HelloUiDefaults.AccessTokenName)
            : null;
    }

    private static OperationResult InvalidSession()
        => OperationResultFactory.Fail(
            new Error(
                IdentityErrorCodes.RefreshTokenInvalid,
                "The session is invalid or expired.",
                ErrorType.Unauthorized));

    private static OperationResult<T> InvalidSession<T>()
        => OperationResultFactory.Fail<T>(
            new Error(
                IdentityErrorCodes.RefreshTokenInvalid,
                "The session is invalid or expired.",
                ErrorType.Unauthorized));

    private static bool TryParseHandle(
        string? value,
        out PasswordLoginHandle handle)
    {
        if (string.Equals(
                value,
                "email",
                StringComparison.OrdinalIgnoreCase))
        {
            handle = PasswordLoginHandle.Email;
            return true;
        }

        if (string.Equals(
                value,
                "username",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                value,
                "userName",
                StringComparison.OrdinalIgnoreCase))
        {
            handle = PasswordLoginHandle.UserName;
            return true;
        }

        handle = default;
        return false;
    }
}
