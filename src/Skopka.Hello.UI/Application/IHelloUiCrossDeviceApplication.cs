using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.DeviceAuthorization;

namespace Skopka.Hello.UI;

public sealed record HelloUiCrossDeviceRequest(
    string DeviceCode,
    string UserCode,
    string ApprovalUrl,
    string QrCodeSvg,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt)
{
    public string QrCodeImageUrl { get; init; } = string.Empty;
}

public sealed record HelloUiCrossDeviceWaiting(
    DeviceAuthorizationState State,
    string DeviceCode,
    string UserCode,
    string ApprovalUrl,
    string QrCodeSvg,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt)
{
    public string QrCodeImageUrl { get; init; } = string.Empty;
}

public sealed record HelloUiCompletedCrossDeviceSignIn(
    HelloUiSignIn SignIn,
    string? ReturnUrl);

public interface IHelloUiCrossDeviceApplication
{
    TimeSpan PollingInterval { get; }

    Task<OperationResult<HelloUiCrossDeviceRequest>> BeginAsync(
        string? returnUrl,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloUiCrossDeviceWaiting>> GetWaitingAsync(
        string deviceCode,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloCrossDeviceApprovalDetails>>
        GetApprovalDetailsAsync(
            string deviceCode,
            HttpContext httpContext,
            CancellationToken cancellationToken);

    Task<OperationResult<HelloCrossDeviceApprovalDetails>>
        GetApprovalDetailsByUserCodeAsync(
            string userCode,
            HttpContext httpContext,
            CancellationToken cancellationToken);

    Task<OperationResult<HelloStepUpChallenge>> BeginApprovalAsync(
        string deviceCode,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<OperationResult> ApproveAsync(
        string deviceCode,
        Guid challengeId,
        string totpCode,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<OperationResult> DenyAsync(
        string deviceCode,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloUiCompletedCrossDeviceSignIn>> CompleteAsync(
        string deviceCode,
        HttpContext httpContext,
        CancellationToken cancellationToken);
}
