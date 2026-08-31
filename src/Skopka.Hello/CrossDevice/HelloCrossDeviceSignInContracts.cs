using Skopka.Abstraction.OperationResult;
using Skopka.Identity.DeviceAuthorization;
using Skopka.Identity.Sessions;

namespace Skopka.Hello;

public sealed record HelloBeginCrossDeviceSignInCommand(
    string? ReturnUrl,
    string? ClientId,
    string? IpAddress,
    string? UserAgent,
    string? DeviceDisplayName,
    IdentitySessionMetadata SessionMetadata,
    string? ClientKey = null);

public sealed record HelloCrossDeviceSignInRequest(
    Guid RequestId,
    string DeviceCode,
    string BrowserVerifier,
    string UserCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record HelloCrossDeviceSignInStatus(
    DeviceAuthorizationState State,
    string UserCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ResolvedAt);

public sealed record HelloCrossDeviceApprovalDetails(
    string DeviceCode,
    string UserCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string? IpAddress,
    string? UserAgent,
    string? DeviceDisplayName);

public sealed record HelloBeginCrossDeviceApprovalCommand(
    string AccessToken,
    string DeviceCode,
    string? ClientKey = null);

public sealed record HelloApproveCrossDeviceSignInCommand(
    string AccessToken,
    string DeviceCode,
    Guid ChallengeId,
    string TotpCode,
    string? ClientKey = null);

public sealed record HelloDenyCrossDeviceSignInCommand(
    string AccessToken,
    string DeviceCode,
    string? ClientKey = null);

public sealed record HelloCompletedCrossDeviceSignIn<TProfile>(
    HelloSignIn<TProfile> SignIn,
    string? ClientId,
    string? ReturnUrl);

public interface IHelloCrossDeviceSignInApplication<TProfile>
{
    Task<OperationResult<HelloCrossDeviceSignInRequest>> BeginAsync(
        HelloBeginCrossDeviceSignInCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloCrossDeviceSignInStatus>> GetStatusAsync(
        string deviceCode,
        string browserVerifier,
        string? clientKey,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloCrossDeviceApprovalDetails>>
        GetApprovalDetailsAsync(
            string accessToken,
            string deviceCode,
            string? clientKey,
            CancellationToken cancellationToken);

    Task<OperationResult<HelloCrossDeviceApprovalDetails>>
        GetApprovalDetailsByUserCodeAsync(
            string accessToken,
            string userCode,
            string? clientKey,
            CancellationToken cancellationToken);

    Task<OperationResult<HelloStepUpChallenge>> BeginApprovalAsync(
        HelloBeginCrossDeviceApprovalCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult> ApproveAsync(
        HelloApproveCrossDeviceSignInCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult> DenyAsync(
        HelloDenyCrossDeviceSignInCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloCompletedCrossDeviceSignIn<TProfile>>>
        CompleteAsync(
            string deviceCode,
            string browserVerifier,
            CancellationToken cancellationToken);
}
