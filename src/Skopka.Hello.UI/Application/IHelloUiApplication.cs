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
    string Login,
    string Password);

public sealed record HelloUiSignIn(
    ClaimsPrincipal Principal,
    HelloSession Session);

public sealed record HelloUiAccount(
    Guid UserId,
    string DisplayName,
    string? UserName,
    string? Email,
    bool EmailConfirmed,
    string? Phone,
    bool PhoneConfirmed,
    long Version,
    IReadOnlyList<HelloUiProfileField> ProfileFields);

public sealed record HelloUiChangeUserNameCommand(
    long ExpectedVersion,
    string UserName);

public sealed record HelloUiChangeEmailCommand(
    long ExpectedVersion,
    string? Email);

public sealed record HelloUiChangePhoneCommand(
    long ExpectedVersion,
    string? Phone);

public sealed record HelloUiUpdateProfileCommand(
    long ExpectedVersion,
    IReadOnlyDictionary<string, string?> Values);

public sealed record HelloUiResetPasswordCommand(
    Guid UserId,
    string Token,
    string NewPassword);

public sealed record HelloUiConfirmEmailCommand(
    Guid UserId,
    string Email,
    string Token);

public sealed record HelloUiConfirmPhoneCommand(
    Guid UserId,
    string Phone,
    string Token);

public sealed record HelloUiCompletePasswordChangeCommand(
    Guid ChallengeId,
    string VerificationCode,
    string CurrentPassword,
    string NewPassword);

public sealed record HelloUiCompletePasswordSetCommand(
    Guid ChallengeId,
    string VerificationCode,
    string NewPassword);

public sealed record HelloUiCompleteAccountSecurityActionCommand(
    Guid ChallengeId,
    string VerificationCode);

public interface IHelloUiApplication
{
    Task<OperationResult> RegisterAsync(
        HelloUiRegisterCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloUiSignIn>> LoginAsync(
        HelloUiLoginCommand command,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloUiAccount>> GetAccountAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => AccountSelfServiceUnavailable();

    Task<OperationResult<HelloUiAccount>> ChangeUserNameAsync(
        HelloUiChangeUserNameCommand command,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => AccountSelfServiceUnavailable();

    Task<OperationResult<HelloUiAccount>> ChangeEmailAsync(
        HelloUiChangeEmailCommand command,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => AccountSelfServiceUnavailable();

    Task<OperationResult<HelloUiAccount>> ChangePhoneAsync(
        HelloUiChangePhoneCommand command,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => AccountSelfServiceUnavailable();

    Task<OperationResult<HelloUiAccount>> UpdateProfileAsync(
        HelloUiUpdateProfileCommand command,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => AccountSelfServiceUnavailable();

    Task<OperationResult> RequestPasswordResetAsync(
        string email,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<OperationResult> ResetPasswordAsync(
        HelloUiResetPasswordCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult> RequestEmailConfirmationAsync(
        string email,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<OperationResult> ConfirmEmailAsync(
        HelloUiConfirmEmailCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult> RequestPhoneConfirmationAsync(
        string phone,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<OperationResult> ConfirmPhoneAsync(
        HelloUiConfirmPhoneCommand command,
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

    Task<OperationResult<HelloCredentialState>> GetCredentialStateAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => AccountSelfServiceUnavailable<HelloCredentialState>();

    Task<OperationResult<HelloStepUpChallenge>> BeginPasswordSetAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => AccountSelfServiceUnavailable<HelloStepUpChallenge>();

    Task<OperationResult> CompletePasswordSetAsync(
        HelloUiCompletePasswordSetCommand command,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => AccountSelfServiceUnavailableResult();

    Task<OperationResult<HelloStepUpChallenge>> BeginPasswordRemovalAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => AccountSelfServiceUnavailable<HelloStepUpChallenge>();

    Task<OperationResult> CompletePasswordRemovalAsync(
        HelloUiCompleteAccountSecurityActionCommand command,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => AccountSelfServiceUnavailableResult();

    Task<OperationResult<HelloStepUpChallenge>> BeginAccountDeletionAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => AccountSelfServiceUnavailable<HelloStepUpChallenge>();

    Task<OperationResult> CompleteAccountDeletionAsync(
        HelloUiCompleteAccountSecurityActionCommand command,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => AccountSelfServiceUnavailableResult();

    private static Task<OperationResult<HelloUiAccount>>
        AccountSelfServiceUnavailable()
        => AccountSelfServiceUnavailable<HelloUiAccount>();

    private static Task<OperationResult<T>>
        AccountSelfServiceUnavailable<T>()
        => Task.FromResult(
            OperationResultFactory.Fail<T>(
                new Error(
                    "hello.ui.account_self_service_unavailable",
                    "Account self-service is not available.",
                    ErrorType.Forbidden)));

    private static Task<OperationResult>
        AccountSelfServiceUnavailableResult()
        => Task.FromResult(
            OperationResultFactory.Fail(
                new Error(
                    "hello.ui.account_self_service_unavailable",
                    "Account self-service is not available.",
                    ErrorType.Forbidden)));
}
