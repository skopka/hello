using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using QRCoder;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;

namespace Skopka.Hello.UI;

internal sealed class HelloUiCrossDeviceApplication<TProfile>(
    IHelloCrossDeviceSignInApplication<TProfile> application,
    IHelloCrossDeviceCookieManager verifierCookies,
    IHelloRequestContext requestContext,
    IHelloUiProfileFactory<TProfile> profiles,
    HelloCrossDeviceSignInOptions options,
    SkopkaHelloOptions helloOptions,
    HelloUiRoutePaths routes)
    : IHelloUiCrossDeviceApplication
{
    public TimeSpan PollingInterval => options.PollingInterval;

    public async Task<OperationResult<HelloUiCrossDeviceRequest>> BeginAsync(
        string? returnUrl,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var sessionMetadata = requestContext.CreateSessionMetadata(
            httpContext,
            options.SessionClientName ?? helloOptions.ClientName);
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        var result = await application.BeginAsync(
            new HelloBeginCrossDeviceSignInCommand(
                returnUrl,
                ClientId: null,
                requestContext.CreateClientKey(httpContext),
                string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
                sessionMetadata.DeviceName,
                sessionMetadata,
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
        if (!result.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloUiCrossDeviceRequest>(
                result.Errors);
        }

        verifierCookies.Write(
            httpContext,
            result.Value.DeviceCode,
            result.Value.BrowserVerifier,
            result.Value.ExpiresAt);
        var approvalUrl = CreateApprovalUrl(
            httpContext,
            result.Value.DeviceCode);
        return OperationResultFactory.Success(
            new HelloUiCrossDeviceRequest(
                result.Value.DeviceCode,
                result.Value.UserCode,
                approvalUrl,
                CreateQrCodeSvg(approvalUrl),
                result.Value.CreatedAt,
                result.Value.ExpiresAt)
            {
                QrCodeImageUrl = CreateQrCodeImageUrl(approvalUrl),
            });
    }

    public async Task<OperationResult<HelloUiCrossDeviceWaiting>>
        GetWaitingAsync(
            string deviceCode,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        if (!verifierCookies.TryRead(
                httpContext,
                deviceCode,
                out var verifier))
        {
            return InvalidRequest<HelloUiCrossDeviceWaiting>();
        }

        var status = await application.GetStatusAsync(
            deviceCode,
            verifier,
            requestContext.CreateClientKey(httpContext),
            cancellationToken);
        if (!status.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloUiCrossDeviceWaiting>(
                status.Errors);
        }

        var approvalUrl = CreateApprovalUrl(httpContext, deviceCode);
        return OperationResultFactory.Success(
            new HelloUiCrossDeviceWaiting(
                status.Value.State,
                deviceCode,
                status.Value.UserCode,
                approvalUrl,
                CreateQrCodeSvg(approvalUrl),
                status.Value.CreatedAt,
                status.Value.ExpiresAt)
            {
                QrCodeImageUrl = CreateQrCodeImageUrl(approvalUrl),
            });
    }

    public async Task<OperationResult<HelloCrossDeviceApprovalDetails>>
        GetApprovalDetailsAsync(
            string deviceCode,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        var accessToken = await ReadAccessTokenAsync(httpContext);
        return accessToken is null
            ? InvalidRequest<HelloCrossDeviceApprovalDetails>()
            : await application.GetApprovalDetailsAsync(
                accessToken,
                deviceCode,
                requestContext.CreateClientKey(httpContext),
                cancellationToken);
    }

    public async Task<OperationResult<HelloCrossDeviceApprovalDetails>>
        GetApprovalDetailsByUserCodeAsync(
            string userCode,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        var accessToken = await ReadAccessTokenAsync(httpContext);
        return accessToken is null
            ? InvalidRequest<HelloCrossDeviceApprovalDetails>()
            : await application.GetApprovalDetailsByUserCodeAsync(
                accessToken,
                userCode,
                requestContext.CreateClientKey(httpContext),
                cancellationToken);
    }

    public async Task<OperationResult<HelloStepUpChallenge>>
        BeginApprovalAsync(
            string deviceCode,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        var accessToken = await ReadAccessTokenAsync(httpContext);
        return accessToken is null
            ? InvalidRequest<HelloStepUpChallenge>()
            : await application.BeginApprovalAsync(
                new HelloBeginCrossDeviceApprovalCommand(
                    accessToken,
                    deviceCode,
                    requestContext.CreateClientKey(httpContext)),
                cancellationToken);
    }

    public async Task<OperationResult> ApproveAsync(
        string deviceCode,
        Guid challengeId,
        string totpCode,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var accessToken = await ReadAccessTokenAsync(httpContext);
        return accessToken is null
            ? InvalidRequest()
            : await application.ApproveAsync(
                new HelloApproveCrossDeviceSignInCommand(
                    accessToken,
                    deviceCode,
                    challengeId,
                    totpCode,
                    requestContext.CreateClientKey(httpContext)),
                cancellationToken);
    }

    public async Task<OperationResult> DenyAsync(
        string deviceCode,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var accessToken = await ReadAccessTokenAsync(httpContext);
        return accessToken is null
            ? InvalidRequest()
            : await application.DenyAsync(
                new HelloDenyCrossDeviceSignInCommand(
                    accessToken,
                    deviceCode,
                    requestContext.CreateClientKey(httpContext)),
                cancellationToken);
    }

    public async Task<OperationResult<HelloUiCompletedCrossDeviceSignIn>>
        CompleteAsync(
            string deviceCode,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        if (!verifierCookies.TryRead(
                httpContext,
                deviceCode,
                out var verifier))
        {
            return InvalidRequest<HelloUiCompletedCrossDeviceSignIn>();
        }

        var completed = await application.CompleteAsync(
            deviceCode,
            verifier,
            cancellationToken);
        if (!completed.IsSuccess)
        {
            return OperationResultFactory.Fail<
                HelloUiCompletedCrossDeviceSignIn>(completed.Errors);
        }

        verifierCookies.Delete(httpContext);
        return OperationResultFactory.Success(
            new HelloUiCompletedCrossDeviceSignIn(
                new HelloUiSignIn(
                    HelloUiPrincipalFactory.Create(
                        completed.Value.SignIn.Account,
                        completed.Value.SignIn.Session.SessionId,
                        profiles),
                    completed.Value.SignIn.Session),
                completed.Value.ReturnUrl));
    }

    private string CreateApprovalUrl(
        HttpContext httpContext,
        string deviceCode)
    {
        var path = QueryHelpers.AddQueryString(
            routes.CrossDeviceApprovalPath,
            "deviceCode",
            deviceCode);
        if (helloOptions.PublicOrigin is not null)
        {
            return new Uri(helloOptions.PublicOrigin, path).AbsoluteUri;
        }

        return $"{httpContext.Request.Scheme}://"
            + $"{httpContext.Request.Host}{httpContext.Request.PathBase}{path}";
    }

    private static string CreateQrCodeSvg(string value)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(
            value,
            QRCodeGenerator.ECCLevel.M);
        return new SvgQRCode(data).GetGraphic(
            8,
            "#000000",
            "#ffffff",
            drawQuietZones: true,
            SvgQRCode.SizingMode.ViewBoxAttribute);
    }

    private static string CreateQrCodeImageUrl(string value)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(
            value,
            QRCodeGenerator.ECCLevel.M);
        using var code = new PngByteQRCode(data);
        var image = code.GetGraphic(
            pixelsPerModule: 12,
            drawQuietZones: true);
        return "data:image/png;base64," + Convert.ToBase64String(image);
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

    private static OperationResult InvalidRequest()
        => OperationResultFactory.Fail(InvalidRequestError());

    private static OperationResult<T> InvalidRequest<T>()
        => OperationResultFactory.Fail<T>(InvalidRequestError());

    private static Error InvalidRequestError()
        => new(
            IdentityErrorCodes.DeviceAuthorizationInvalid,
            "The device authorization request is invalid or expired.",
            ErrorType.Unauthorized);
}
