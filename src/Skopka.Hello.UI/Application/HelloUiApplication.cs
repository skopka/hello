using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Skopka.Abstraction.OperationResult;
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

        if (!helloOptions.SelfRegistrationEnabled)
        {
            return OperationResultFactory.Fail(
                HelloRegistrationErrors.Disabled());
        }

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

        var result = await application.LoginAsync(
            new HelloLoginCommand(
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
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => application.RequestPasswordResetAsync(
            email,
            requestContext.CreateClientKey(httpContext),
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
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => application.RequestEmailConfirmationAsync(
            email,
            requestContext.CreateClientKey(httpContext),
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

    public Task<OperationResult> RequestPhoneConfirmationAsync(
        string phone,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => application.RequestPhoneConfirmationAsync(
            phone,
            requestContext.CreateClientKey(httpContext),
            cancellationToken);

    public async Task<OperationResult> ConfirmPhoneAsync(
        HelloUiConfirmPhoneCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await application.ConfirmPhoneAsync(
            new HelloConfirmPhoneCommand(
                command.UserId,
                command.Phone,
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

}
