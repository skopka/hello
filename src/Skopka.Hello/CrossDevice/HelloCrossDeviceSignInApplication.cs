using Skopka.Abstraction.OperationResult;
using Skopka.Identity.DeviceAuthorization;
using Skopka.Identity.Errors;
using Skopka.Identity.Sessions;
using Skopka.Identity.StepUp;
using Skopka.Identity.StepUp.Commands;
using Skopka.Identity.Users;
using Skopka.Identity.Verification;

namespace Skopka.Hello;

internal sealed class HelloCrossDeviceSignInApplication<TProfile>(
    IIdentityDeviceAuthorizationService<TProfile> deviceAuthorization,
    IIdentityStepUpService<TProfile> stepUp,
    IIdentityVerificationService<TProfile> verification,
    IIdentitySessionService<TProfile> sessions,
    IEnumerable<IHelloAccessTokenValidator<TProfile>> accessTokenValidators)
    : IHelloCrossDeviceSignInApplication<TProfile>
{
    public async Task<OperationResult<HelloCrossDeviceSignInRequest>>
        BeginAsync(
            HelloBeginCrossDeviceSignInCommand command,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.ReturnUrl is not null
            && !HelloLocalReturnUrl.IsLocal(command.ReturnUrl))
        {
            return OperationResultFactory.Fail<
                HelloCrossDeviceSignInRequest>(
                    InvalidReturnUrl());
        }

        var result = await deviceAuthorization.CreateAsync(
            new CreateDeviceAuthorizationRequestCommand(
                new DeviceAuthorizationMetadata(
                    command.IpAddress,
                    command.UserAgent,
                    command.DeviceDisplayName,
                    command.ClientId,
                    command.ReturnUrl,
                    command.SessionMetadata),
                command.ClientKey),
            cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success(
                new HelloCrossDeviceSignInRequest(
                    result.Value.RequestId,
                    result.Value.DeviceCode,
                    result.Value.BrowserVerifier,
                    result.Value.UserCode,
                    result.Value.CreatedAt,
                    result.Value.ExpiresAt))
            : OperationResultFactory.Fail<HelloCrossDeviceSignInRequest>(
                result.Errors);
    }

    public async Task<OperationResult<HelloCrossDeviceSignInStatus>>
        GetStatusAsync(
            string deviceCode,
            string browserVerifier,
            string? clientKey,
            CancellationToken cancellationToken)
    {
        var result = await deviceAuthorization.GetStatusAsync(
            new GetDeviceAuthorizationStatusCommand(
                deviceCode,
                browserVerifier,
                clientKey),
            cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success(
                new HelloCrossDeviceSignInStatus(
                    result.Value.State,
                    result.Value.UserCode,
                    result.Value.CreatedAt,
                    result.Value.ExpiresAt,
                    result.Value.ResolvedAt))
            : OperationResultFactory.Fail<HelloCrossDeviceSignInStatus>(
                result.Errors);
    }

    public async Task<OperationResult<HelloCrossDeviceApprovalDetails>>
        GetApprovalDetailsAsync(
            string accessToken,
            string deviceCode,
            string? clientKey,
            CancellationToken cancellationToken)
    {
        var user = await ValidateTokenAsync(
            accessToken,
            cancellationToken);
        if (!user.IsSuccess)
        {
            return OperationResultFactory.Fail<
                HelloCrossDeviceApprovalDetails>(user.Errors);
        }

        var details = await deviceAuthorization.GetApprovalDetailsAsync(
            new GetDeviceAuthorizationApprovalDetailsCommand(
                deviceCode,
                clientKey),
            cancellationToken);
        return details.IsSuccess
            ? OperationResultFactory.Success(
                ToApprovalDetails(details.Value))
            : OperationResultFactory.Fail<HelloCrossDeviceApprovalDetails>(
                details.Errors);
    }

    public async Task<OperationResult<HelloStepUpChallenge>>
        BeginApprovalAsync(
            HelloBeginCrossDeviceApprovalCommand command,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var user = await ValidateTokenAsync(
            command.AccessToken,
            cancellationToken);
        if (!user.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                user.Errors);
        }

        var details = await deviceAuthorization.GetApprovalDetailsAsync(
            new GetDeviceAuthorizationApprovalDetailsCommand(
                command.DeviceCode,
                command.ClientKey),
            cancellationToken);
        if (!details.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                details.Errors);
        }

        var challenge = await stepUp.BeginAsync(
            new BeginStepUpCommand(
                user.Value.Id,
                DeviceAuthorizationActions.Approve,
                command.DeviceCode,
                VerificationMethods.TimeBasedOneTimePassword,
                command.ClientKey),
            cancellationToken);
        return challenge.IsSuccess
            ? OperationResultFactory.Success(
                new HelloStepUpChallenge(
                    challenge.Value.ChallengeId,
                    challenge.Value.ExpiresAt,
                    HelloDeliveryChannel.Authenticator))
            : OperationResultFactory.Fail<HelloStepUpChallenge>(
                challenge.Errors);
    }

    public async Task<OperationResult> ApproveAsync(
        HelloApproveCrossDeviceSignInCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var user = await ValidateTokenAsync(
            command.AccessToken,
            cancellationToken);
        if (!user.IsSuccess)
        {
            return OperationResultFactory.Fail(user.Errors);
        }

        var proof = await verification.VerifyAsync(
            new VerifyVerificationChallengeCommand(
                command.ChallengeId,
                user.Value.Id,
                command.TotpCode,
                command.ClientKey),
            cancellationToken);
        if (!proof.IsSuccess)
        {
            return OperationResultFactory.Fail(proof.Errors);
        }

        var decision = await stepUp.AuthorizeAsync(
            new AuthorizeStepUpCommand(
                user.Value.Id,
                DeviceAuthorizationActions.Approve,
                command.DeviceCode,
                proof.Value.ChallengeId,
                proof.Value.Token),
            cancellationToken);
        if (!decision.IsSuccess)
        {
            return OperationResultFactory.Fail(decision.Errors);
        }

        return await deviceAuthorization.ApproveAsync(
            new ApproveDeviceAuthorizationRequestCommand(
                command.DeviceCode,
                user.Value.Id,
                decision.Value),
            cancellationToken);
    }

    public async Task<OperationResult> DenyAsync(
        HelloDenyCrossDeviceSignInCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var user = await ValidateTokenAsync(
            command.AccessToken,
            cancellationToken);
        if (!user.IsSuccess)
        {
            return OperationResultFactory.Fail(user.Errors);
        }

        return await deviceAuthorization.DenyAsync(
            new DenyDeviceAuthorizationRequestCommand(
                command.DeviceCode,
                user.Value.Id),
            cancellationToken);
    }

    public async Task<OperationResult<
        HelloCompletedCrossDeviceSignIn<TProfile>>> CompleteAsync(
            string deviceCode,
            string browserVerifier,
            CancellationToken cancellationToken)
    {
        var consumed = await deviceAuthorization.ConsumeAsync(
            new ConsumeDeviceAuthorizationRequestCommand(
                deviceCode,
                browserVerifier),
            cancellationToken);
        if (!consumed.IsSuccess)
        {
            return OperationResultFactory.Fail<
                HelloCompletedCrossDeviceSignIn<TProfile>>(
                    consumed.Errors);
        }

        var user = await sessions.ValidateAccessTokenAsync(
            consumed.Value.Session.AccessToken,
            cancellationToken);
        if (!user.IsSuccess)
        {
            return OperationResultFactory.Fail<
                HelloCompletedCrossDeviceSignIn<TProfile>>(user.Errors);
        }

        return OperationResultFactory.Success(
            new HelloCompletedCrossDeviceSignIn<TProfile>(
                new HelloSignIn<TProfile>(
                    ToAccount(user.Value),
                    ToSession(consumed.Value.Session)),
                consumed.Value.ClientId,
                consumed.Value.ReturnUrl));
    }

    private async Task<OperationResult<IdentityUser<TProfile>>>
        ValidateTokenAsync(
            string accessToken,
            CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return OperationResultFactory.Fail<IdentityUser<TProfile>>(
                new Error(
                    IdentityErrorCodes.AccessTokenInvalid,
                    "The access token is invalid.",
                    ErrorType.Unauthorized));
        }

        OperationResult<IdentityUser<TProfile>>? firstFailure = null;
        foreach (var validator in accessTokenValidators)
        {
            var result = await validator.ValidateAsync(
                accessToken,
                cancellationToken);
            if (result.IsSuccess)
            {
                return result;
            }

            firstFailure ??= result;
        }

        return firstFailure
            ?? await sessions.ValidateAccessTokenAsync(
                accessToken,
                cancellationToken);
    }

    private static HelloCrossDeviceApprovalDetails ToApprovalDetails(
        DeviceAuthorizationApprovalDetails details)
        => new(
            details.DeviceCode,
            details.UserCode,
            details.CreatedAt,
            details.ExpiresAt,
            details.IpAddress,
            details.UserAgent,
            details.DeviceDisplayName);

    private static HelloAccount<TProfile> ToAccount(
        IdentityUser<TProfile> user)
        => new(
            user.Id,
            user.Flags,
            user.UserName,
            user.Email,
            user.EmailConfirmed,
            user.Phone,
            user.PhoneConfirmed,
            user.Profile,
            user.Version,
            user.CreatedAt,
            user.ModifiedAt);

    private static HelloSession ToSession(IssuedIdentitySession session)
        => new(
            session.SessionId,
            session.AccessToken,
            session.AccessTokenExpiresAt,
            session.RefreshToken,
            session.RefreshTokenExpiresAt);

    private static Error InvalidReturnUrl()
        => new(
            "hello.cross_device.return_url_invalid",
            "The cross-device return URL must be local to this application.",
            ErrorType.Validation);
}

internal static class HelloLocalReturnUrl
{
    public static bool IsLocal(string url)
    {
        if (string.IsNullOrEmpty(url)
            || url.Length > 2_048
            || url.Any(char.IsControl))
        {
            return false;
        }

        if (url[0] == '/')
        {
            return url.Length == 1
                || url[1] is not ('/' or '\\');
        }

        return url.Length > 1
            && url[0] == '~'
            && url[1] == '/'
            && (url.Length == 2 || url[2] is not ('/' or '\\'));
    }
}
