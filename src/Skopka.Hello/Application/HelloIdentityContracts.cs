using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Sessions;
using Skopka.Identity.Users;

namespace Skopka.Hello;

public static class HelloPasswordChangeErrorCodes
{
    public const string RestartRequired =
        "hello.account.password_change_restart_required";

    public const string SessionCleanupRequired =
        "hello.account.password_change_session_cleanup_required";
}

public static class HelloAccountSecurityActionErrorCodes
{
    public const string RestartRequired =
        "hello.account.security_action_restart_required";

    public const string SessionCleanupRequired =
        "hello.account.security_action_session_cleanup_required";

    public const string LastSignInMethod =
        "hello.account.last_sign_in_method";

    public const string PasswordLoginHandleRequired =
        "hello.account.password_login_handle_required";
}

public sealed record HelloRegisterCommand<TProfile>(
    string? UserName,
    string? Email,
    string? Phone,
    TProfile Profile,
    string Password);

public sealed record HelloLoginCommand(
    string Login,
    string Password,
    string? ClientKey,
    IdentitySessionMetadata SessionMetadata);

public sealed record HelloResetPasswordCommand(
    Guid UserId,
    string Token,
    string NewPassword);

public sealed record HelloConfirmEmailCommand(
    Guid UserId,
    string Email,
    string Token);

public sealed record HelloConfirmPhoneCommand(
    Guid UserId,
    string Phone,
    string Token);

public sealed record HelloChangeUserNameCommand(
    string AccessToken,
    long ExpectedVersion,
    string UserName);

public sealed record HelloChangeEmailCommand(
    string AccessToken,
    long ExpectedVersion,
    string? Email);

public sealed record HelloChangePhoneCommand(
    string AccessToken,
    long ExpectedVersion,
    string? Phone);

public sealed record HelloReplaceProfileCommand<TProfile>(
    string AccessToken,
    long ExpectedVersion,
    TProfile Profile);

public sealed record HelloBeginPasswordChangeCommand(
    string AccessToken,
    string? ClientKey);

public sealed record HelloCompletePasswordChangeCommand(
    string AccessToken,
    Guid ChallengeId,
    string VerificationCode,
    string CurrentPassword,
    string NewPassword);

public sealed record HelloBeginPasswordSetCommand(
    string AccessToken,
    string? ClientKey);

public sealed record HelloCompletePasswordSetCommand(
    string AccessToken,
    Guid ChallengeId,
    string VerificationCode,
    string NewPassword);

public sealed record HelloBeginPasswordRemovalCommand(
    string AccessToken,
    string? ClientKey);

public sealed record HelloCompletePasswordRemovalCommand(
    string AccessToken,
    Guid ChallengeId,
    string VerificationCode);

public sealed record HelloBeginAccountDeletionCommand(
    string AccessToken,
    string? ClientKey);

public sealed record HelloCompleteAccountDeletionCommand(
    string AccessToken,
    Guid ChallengeId,
    string VerificationCode);

public sealed record HelloStepUpChallenge(
    Guid ChallengeId,
    DateTimeOffset ExpiresAt,
    HelloDeliveryChannel DeliveryChannel);

public sealed record HelloAccount<TProfile>(
    Guid Id,
    UserFlags Flags,
    string? UserName,
    string? Email,
    bool EmailConfirmed,
    string? Phone,
    bool PhoneConfirmed,
    TProfile Profile,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt);

public sealed record HelloSession(
    Guid SessionId,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record HelloSignIn<TProfile>(
    HelloAccount<TProfile> Account,
    HelloSession Session);

public sealed record HelloSessionInfo(
    Guid SessionId,
    string? ClientName,
    string? DeviceName,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastRefreshedAt);

public sealed record HelloCredentialState(
    bool HasPassword,
    bool CanRemovePassword);

public interface IHelloAccessTokenValidator<TProfile>
{
    Task<OperationResult<IdentityUser<TProfile>>> ValidateAsync(
        string accessToken,
        CancellationToken cancellationToken);
}

public interface IHelloIdentityApplication<TProfile>
{
    Task<OperationResult<HelloAccount<TProfile>>> RegisterAsync(
        HelloRegisterCommand<TProfile> command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloSignIn<TProfile>>> LoginAsync(
        HelloLoginCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloSession>> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloAccount<TProfile>>> ValidateAccessTokenAsync(
        string accessToken,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloAccount<TProfile>>> ChangeUserNameAsync(
        HelloChangeUserNameCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloAccount<TProfile>>> ChangeEmailAsync(
        HelloChangeEmailCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloAccount<TProfile>>> ChangePhoneAsync(
        HelloChangePhoneCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloAccount<TProfile>>> ReplaceProfileAsync(
        HelloReplaceProfileCommand<TProfile> command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloCredentialState>> GetCredentialStateAsync(
        string accessToken,
        CancellationToken cancellationToken);

    Task<OperationResult> LogoutAsync(
        string refreshToken,
        CancellationToken cancellationToken);

    Task<OperationResult> LogoutAllAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<OperationResult<IReadOnlyList<HelloSessionInfo>>> ListSessionsAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<OperationResult> RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<OperationResult> RequestPasswordResetAsync(
        string email,
        string? clientKey,
        CancellationToken cancellationToken);

    Task<OperationResult> ResetPasswordAsync(
        HelloResetPasswordCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult> RequestEmailConfirmationAsync(
        string email,
        string? clientKey,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloAccount<TProfile>>> ConfirmEmailAsync(
        HelloConfirmEmailCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult> RequestPhoneConfirmationAsync(
        string phone,
        string? clientKey,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloAccount<TProfile>>> ConfirmPhoneAsync(
        HelloConfirmPhoneCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloStepUpChallenge>>
        BeginPasswordChangeAsync(
            HelloBeginPasswordChangeCommand command,
            CancellationToken cancellationToken);

    Task<OperationResult> CompletePasswordChangeAsync(
        HelloCompletePasswordChangeCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloStepUpChallenge>> BeginPasswordSetAsync(
        HelloBeginPasswordSetCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult> CompletePasswordSetAsync(
        HelloCompletePasswordSetCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloStepUpChallenge>> BeginPasswordRemovalAsync(
        HelloBeginPasswordRemovalCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult> CompletePasswordRemovalAsync(
        HelloCompletePasswordRemovalCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloStepUpChallenge>> BeginAccountDeletionAsync(
        HelloBeginAccountDeletionCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult> CompleteAccountDeletionAsync(
        HelloCompleteAccountDeletionCommand command,
        CancellationToken cancellationToken);
}
