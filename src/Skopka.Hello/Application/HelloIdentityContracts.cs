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

public sealed record HelloBeginPasswordChangeCommand(
    string AccessToken,
    string? ClientKey);

public sealed record HelloCompletePasswordChangeCommand(
    string AccessToken,
    Guid ChallengeId,
    string VerificationCode,
    string CurrentPassword,
    string NewPassword);

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
}
