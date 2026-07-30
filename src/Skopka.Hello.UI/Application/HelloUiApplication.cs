using Microsoft.AspNetCore.Http;
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
