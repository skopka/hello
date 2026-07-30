using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;

namespace Skopka.Hello.UI;

public sealed record HelloUiRegisterCommand(
    string? UserName,
    string? Email,
    string? Phone,
    string DisplayName,
    string? Locale,
    string Password);

public sealed record HelloUiLoginCommand(
    string Handle,
    string Login,
    string Password);

public sealed record HelloUiSignIn(
    ClaimsPrincipal Principal,
    HelloSession Session);

public sealed record HelloUiResetPasswordCommand(
    Guid UserId,
    string Token,
    string NewPassword);

public sealed record HelloUiConfirmEmailCommand(
    Guid UserId,
    string Email,
    string Token);

public sealed record HelloUiCompletePasswordChangeCommand(
    Guid ChallengeId,
    string VerificationCode,
    string CurrentPassword,
    string NewPassword);

public interface IHelloUiApplication
{
    Task<OperationResult> RegisterAsync(
        HelloUiRegisterCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloUiSignIn>> LoginAsync(
        HelloUiLoginCommand command,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<OperationResult> RequestPasswordResetAsync(
        string email,
        CancellationToken cancellationToken);

    Task<OperationResult> ResetPasswordAsync(
        HelloUiResetPasswordCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult> RequestEmailConfirmationAsync(
        string email,
        CancellationToken cancellationToken);

    Task<OperationResult> ConfirmEmailAsync(
        HelloUiConfirmEmailCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<IReadOnlyList<HelloSessionInfo>>> ListSessionsAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<OperationResult> RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<OperationResult> LogoutAsync(
        string refreshToken,
        CancellationToken cancellationToken);

    Task<OperationResult> LogoutAllAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloStepUpChallenge>>
        BeginPasswordChangeAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken);

    Task<OperationResult> CompletePasswordChangeAsync(
        HelloUiCompletePasswordChangeCommand command,
        HttpContext httpContext,
        CancellationToken cancellationToken);
}
