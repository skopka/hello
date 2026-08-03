using Skopka.Abstraction.OperationResult;
using Skopka.Identity;
using Skopka.Identity.Authentication;
using Skopka.Identity.Credentials;
using Skopka.Identity.Errors;
using Skopka.Identity.Registration;
using Skopka.Identity.Sessions;
using Skopka.Identity.StepUp;
using Skopka.Identity.StepUp.Commands;
using Skopka.Identity.Users;
using Skopka.Identity.Users.Commands;
using Skopka.Identity.Verification;

namespace Skopka.Hello;

internal sealed class HelloIdentityApplication<TProfile>(
    IIdentityRegistrationService<TProfile> registration,
    IPasswordAuthenticationService<TProfile> authentication,
    IIdentitySessionService<TProfile> sessions,
    IPasswordCredentialService<TProfile> credentials,
    IIdentityUserService<TProfile> users,
    IIdentityStepUpService<TProfile> stepUp,
    IIdentityVerificationService<TProfile> verification,
    HelloAnonymousAccountMessageRequester<TProfile>
        anonymousMessageRequester,
    IHelloAccountMessageSender messageSender,
    HelloDeliveryOptions deliveryOptions,
    SkopkaHelloOptions options,
    HelloRegistrationAdmission<TProfile>? registrationAdmission = null)
    : IHelloIdentityApplication<TProfile>
{
    public async Task<OperationResult<HelloAccount<TProfile>>> RegisterAsync(
        HelloRegisterCommand<TProfile> command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!options.SelfRegistrationEnabled)
        {
            return OperationResultFactory.Fail<
                HelloAccount<TProfile>>(
                    HelloRegistrationErrors.Disabled());
        }

        if (registrationAdmission is not null)
        {
            var admitted = await registrationAdmission.CheckAsync(
                HelloRegistrationKind.Password,
                cancellationToken);
            if (!admitted.IsSuccess)
            {
                return OperationResultFactory.Fail<
                    HelloAccount<TProfile>>(admitted.Errors);
            }
        }

        var result = await registration.RegisterPasswordAsync(
            new RegisterPasswordUserCommand<TProfile>(
                new CreateUserCommand<TProfile>(
                    command.UserName,
                    command.Email,
                    command.Phone,
                    command.Profile),
                command.Password),
            cancellationToken);

        return result.IsSuccess
            ? OperationResultFactory.Success(ToAccount(result.Value))
            : OperationResultFactory.Fail<HelloAccount<TProfile>>(
                result.Errors);
    }

    public async Task<OperationResult<HelloSignIn<TProfile>>> LoginAsync(
        HelloLoginCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var authenticated = await authentication.AuthenticateAsync(
            new AuthenticatePasswordCommand(
                PasswordLoginHandle.Automatic,
                command.Login,
                command.Password,
                command.ClientKey),
            cancellationToken);
        if (!authenticated.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloSignIn<TProfile>>(
                authenticated.Errors);
        }

        var issued = await sessions.CreateAsync(
            new CreateIdentitySessionCommand(
                authenticated.Value.Id,
                authenticated.Value.SecurityStamp,
                command.SessionMetadata),
            cancellationToken);
        return issued.IsSuccess
            ? OperationResultFactory.Success(
                new HelloSignIn<TProfile>(
                    ToAccount(authenticated.Value),
                    ToSession(issued.Value)))
            : OperationResultFactory.Fail<HelloSignIn<TProfile>>(
                issued.Errors);
    }

    public async Task<OperationResult<HelloSession>> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        var result = await sessions.RefreshAsync(
            new RefreshIdentitySessionCommand(refreshToken),
            cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success(ToSession(result.Value))
            : OperationResultFactory.Fail<HelloSession>(result.Errors);
    }

    public async Task<OperationResult<HelloAccount<TProfile>>>
        ValidateAccessTokenAsync(
            string accessToken,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        var result = await sessions.ValidateAccessTokenAsync(
            accessToken,
            cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success(ToAccount(result.Value))
            : OperationResultFactory.Fail<HelloAccount<TProfile>>(
                result.Errors);
    }

    public Task<OperationResult> LogoutAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        return sessions.RevokeAsync(
            new RevokeIdentitySessionCommand(refreshToken),
            cancellationToken);
    }

    public Task<OperationResult> LogoutAllAsync(
        Guid userId,
        CancellationToken cancellationToken)
        => sessions.RevokeAllAsync(
            new RevokeAllIdentitySessionsCommand(userId),
            cancellationToken);

    public async Task<OperationResult<IReadOnlyList<HelloSessionInfo>>>
        ListSessionsAsync(
            Guid userId,
            CancellationToken cancellationToken)
    {
        var result = await sessions.ListAsync(
            new ListIdentitySessionsCommand(userId),
            cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success<IReadOnlyList<HelloSessionInfo>>(
                result.Value.Select(ToSessionInfo).ToArray())
            : OperationResultFactory.Fail<IReadOnlyList<HelloSessionInfo>>(
                result.Errors);
    }

    public Task<OperationResult> RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken)
        => sessions.RevokeByIdAsync(
            new RevokeIdentitySessionByIdCommand(userId, sessionId),
            cancellationToken);

    public Task<OperationResult> RequestPasswordResetAsync(
        string email,
        string? clientKey,
        CancellationToken cancellationToken)
        => anonymousMessageRequester.EnqueueAsync(
            HelloAccountMessageKind.PasswordReset,
            email,
            clientKey,
            cancellationToken);

    public Task<OperationResult> RequestEmailConfirmationAsync(
        string email,
        string? clientKey,
        CancellationToken cancellationToken)
        => anonymousMessageRequester.EnqueueAsync(
            HelloAccountMessageKind.EmailConfirmation,
            email,
            clientKey,
            cancellationToken);

    public Task<OperationResult> RequestPhoneConfirmationAsync(
        string phone,
        string? clientKey,
        CancellationToken cancellationToken)
        => anonymousMessageRequester.EnqueueAsync(
            HelloAccountMessageKind.PhoneConfirmation,
            phone,
            clientKey,
            cancellationToken);

    public Task<OperationResult> ResetPasswordAsync(
        HelloResetPasswordCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return credentials.ResetPasswordAsync(
            new ResetPasswordCommand(
                command.UserId,
                command.Token,
                command.NewPassword),
            cancellationToken);
    }

    public async Task<OperationResult<HelloAccount<TProfile>>>
        ConfirmEmailAsync(
            HelloConfirmEmailCommand command,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await users.ConfirmEmailAsync(
            new ConfirmEmailCommand(
                command.UserId,
                command.Email,
                command.Token),
            cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success(ToAccount(result.Value))
            : OperationResultFactory.Fail<HelloAccount<TProfile>>(
                result.Errors);
    }

    public async Task<OperationResult<HelloAccount<TProfile>>>
        ConfirmPhoneAsync(
            HelloConfirmPhoneCommand command,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await users.ConfirmPhoneAsync(
            new ConfirmPhoneCommand(
                command.UserId,
                command.Phone,
                command.Token),
            cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success(ToAccount(result.Value))
            : OperationResultFactory.Fail<HelloAccount<TProfile>>(
                result.Errors);
    }

    public async Task<OperationResult<HelloStepUpChallenge>>
        BeginPasswordChangeAsync(
            HelloBeginPasswordChangeCommand command,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validated = await sessions.ValidateAccessTokenAsync(
            command.AccessToken,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                validated.Errors);
        }

        var user = validated.Value;
        if (!HelloAccountSecurity.TryGetConfirmedDestination(
                user,
                deliveryOptions.VerificationChannel,
                out var destination))
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                HelloAccountSecurity.ConfirmedDestinationRequired(
                    deliveryOptions.VerificationChannel));
        }

        var deliveryAvailable = messageSender.CheckAvailability(
            deliveryOptions.VerificationChannel);
        if (!deliveryAvailable.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                deliveryAvailable.Errors);
        }

        var issued = await stepUp.BeginAsync(
            new BeginStepUpCommand(
                user.Id,
                HelloAccountSecurity.PasswordChangeAction,
                HelloAccountSecurity.CreateBinding(
                    user.Id,
                    deliveryOptions.VerificationChannel,
                    destination!),
                VerificationMethods.OneTimeCode,
                command.ClientKey),
            cancellationToken);
        if (!issued.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                issued.Errors);
        }

        if (string.IsNullOrWhiteSpace(issued.Value.DeliveryCode))
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                new Error(
                    IdentityErrorCodes.VerificationMethodUnavailable,
                    "The verification code could not be delivered.",
                    ErrorType.Failure));
        }

        var delivered = await messageSender.SendAsync(
            new HelloAccountMessage(
                Guid.NewGuid(),
                HelloAccountMessageKind.PasswordChangeVerification,
                deliveryOptions.VerificationChannel,
                destination!,
                null,
                issued.Value.ExpiresAt,
                issued.Value.DeliveryCode),
            cancellationToken);
        return delivered.IsSuccess
            ? OperationResultFactory.Success(
                new HelloStepUpChallenge(
                    issued.Value.ChallengeId,
                    issued.Value.ExpiresAt,
                    deliveryOptions.VerificationChannel))
            : OperationResultFactory.Fail<HelloStepUpChallenge>(
                delivered.Errors);
    }

    public async Task<OperationResult> CompletePasswordChangeAsync(
        HelloCompletePasswordChangeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validated = await sessions.ValidateAccessTokenAsync(
            command.AccessToken,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            return OperationResultFactory.Fail(validated.Errors);
        }

        var user = validated.Value;
        if (!HelloAccountSecurity.TryGetConfirmedDestination(
                user,
                deliveryOptions.VerificationChannel,
                out var destination))
        {
            return OperationResultFactory.Fail(
                HelloAccountSecurity.ConfirmedDestinationRequired(
                    deliveryOptions.VerificationChannel));
        }

        var verified = await verification.VerifyAsync(
            new VerifyVerificationChallengeCommand(
                command.ChallengeId,
                user.Id,
                command.VerificationCode),
            cancellationToken);
        if (!verified.IsSuccess)
        {
            return HelloAccountSecurity.IsRetryableVerificationResponse(
                verified.Errors)
                ? OperationResultFactory.Fail(verified.Errors)
                : PasswordChangeRestartRequired(verified.Errors);
        }

        var authorized = await stepUp.AuthorizeAsync(
            new AuthorizeStepUpCommand(
                user.Id,
                HelloAccountSecurity.PasswordChangeAction,
                HelloAccountSecurity.CreateBinding(
                    user.Id,
                    deliveryOptions.VerificationChannel,
                    destination!),
                command.ChallengeId,
                verified.Value.Token),
            cancellationToken);
        if (!authorized.IsSuccess)
        {
            return PasswordChangeRestartRequired(authorized.Errors);
        }

        var changed = await credentials.ChangePasswordAsync(
            new ChangePasswordCommand(
                user.Id,
                user.Version,
                command.CurrentPassword,
                command.NewPassword),
            cancellationToken);
        if (!changed.IsSuccess)
        {
            return PasswordChangeRestartRequired(changed.Errors);
        }

        var revoked = await sessions.RevokeAllAsync(
            new RevokeAllIdentitySessionsCommand(user.Id),
            cancellationToken);
        return revoked.IsSuccess
            ? revoked
            : PasswordChangeSessionCleanupRequired(revoked.Errors);
    }

    private static OperationResult PasswordChangeRestartRequired(
        IReadOnlyCollection<Error> causes)
        => OperationResultFactory.Fail(
            causes.Prepend(
                    new Error(
                        HelloPasswordChangeErrorCodes.RestartRequired,
                        "The verification code can no longer be used. Request a new code and try again.",
                        ErrorType.Conflict))
                .ToArray());

    private static OperationResult PasswordChangeSessionCleanupRequired(
        IReadOnlyCollection<Error> causes)
        => OperationResultFactory.Fail(
            causes.Prepend(
                    new Error(
                        HelloPasswordChangeErrorCodes
                            .SessionCleanupRequired,
                        "The password was changed, but session cleanup could not be completed. Sign in again with the new password.",
                        ErrorType.Conflict))
                .ToArray());

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

    private static HelloSessionInfo ToSessionInfo(
        IdentitySessionInfo session)
        => new(
            session.SessionId,
            session.Metadata.ClientName,
            session.Metadata.DeviceName,
            session.ExpiresAt,
            session.CreatedAt,
            session.LastRefreshedAt);
}
