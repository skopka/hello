using Skopka.Abstraction.OperationResult;
using Skopka.Identity;
using Skopka.Identity.Authentication;
using Skopka.Identity.Credentials;
using Skopka.Identity.Errors;
using Skopka.Identity.Registration;
using Skopka.Identity.Sessions;
using Skopka.Identity.SignInMethods;
using Skopka.Identity.StepUp;
using Skopka.Identity.StepUp.Commands;
using Skopka.Identity.Totp;
using Skopka.Identity.Users;
using Skopka.Identity.Users.Commands;
using Skopka.Identity.Verification;
using QRCoder;

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
    HelloRegistrationAdmission<TProfile>? registrationAdmission = null,
    IIdentitySignInMethodQueryService<TProfile>? signInMethods = null,
    IEnumerable<IHelloAccessTokenValidator<TProfile>>?
        accessTokenValidators = null,
    IIdentityTotpService<TProfile>? totp = null,
    HelloStepUpMethodResolver<TProfile>? stepUpMethodResolver = null,
    IHelloRegistrationConsentPolicy? registrationConsentPolicy = null,
    IHelloRegistrationConsentProfileEnricher<TProfile>?
        registrationConsentProfileEnricher = null)
    : IHelloIdentityApplication<TProfile>
{
    private readonly HelloStepUpMethodResolver<TProfile> stepUpMethods =
        stepUpMethodResolver
        ?? new HelloStepUpMethodResolver<TProfile>(
            deliveryOptions,
            messageSender);

    private readonly IHelloRegistrationConsentPolicy registrationConsent =
        registrationConsentPolicy
        ?? new HelloRegistrationConsentPolicy(options, []);

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

        if (string.IsNullOrWhiteSpace(command.UserName)
            && string.IsNullOrWhiteSpace(command.Email)
            && string.IsNullOrWhiteSpace(command.Phone))
        {
            return OperationResultFactory.Fail<
                HelloAccount<TProfile>>(
                    HelloRegistrationErrors.LoginHandleRequired());
        }

        var submittedConsent = command.RegistrationConsent
            ?? HelloRegistrationConsent.None;
        var consentValidation = registrationConsent.Validate(
            submittedConsent);
        if (!consentValidation.IsSuccess)
        {
            return OperationResultFactory.Fail<
                HelloAccount<TProfile>>(consentValidation.Errors);
        }

        var consent = consentValidation.Value;

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

        var profile = command.Profile;
        if (registrationConsentProfileEnricher is not null)
        {
            var enriched = registrationConsentProfileEnricher.Enrich(
                profile,
                consent);
            if (!enriched.IsSuccess)
            {
                return OperationResultFactory.Fail<
                    HelloAccount<TProfile>>(enriched.Errors);
            }

            profile = enriched.Value;
        }

        var result = await registration.RegisterPasswordAsync(
            new RegisterPasswordUserCommand<TProfile>(
                new CreateUserCommand<TProfile>(
                    command.UserName,
                    command.Email,
                    command.Phone,
                    profile),
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

        var result = await ValidateTokenAsync(
            accessToken,
            cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success(ToAccount(result.Value))
            : OperationResultFactory.Fail<HelloAccount<TProfile>>(
                result.Errors);
    }

    public async Task<OperationResult<HelloAccount<TProfile>>>
        ChangeUserNameAsync(
            HelloChangeUserNameCommand command,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validated = await ValidateTokenAsync(
            command.AccessToken,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloAccount<TProfile>>(
                validated.Errors);
        }

        var handleAllowed = await EnsureLocalHandleChangeAllowedAsync(
            validated.Value,
            command.UserName,
            validated.Value.Email,
            validated.Value.Phone,
            cancellationToken);
        if (!handleAllowed.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloAccount<TProfile>>(
                handleAllowed.Errors);
        }

        var changed = await users.ChangeUserNameAsync(
            new ChangeUserNameCommand(
                validated.Value.Id,
                command.ExpectedVersion,
                command.UserName),
            cancellationToken);
        return ToAccountResult(changed);
    }

    public async Task<OperationResult<HelloAccount<TProfile>>>
        ChangeEmailAsync(
            HelloChangeEmailCommand command,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validated = await ValidateTokenAsync(
            command.AccessToken,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloAccount<TProfile>>(
                validated.Errors);
        }

        var handleAllowed = await EnsureLocalHandleChangeAllowedAsync(
            validated.Value,
            validated.Value.UserName,
            command.Email,
            validated.Value.Phone,
            cancellationToken);
        if (!handleAllowed.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloAccount<TProfile>>(
                handleAllowed.Errors);
        }

        var changed = await users.ChangeEmailAsync(
            new ChangeEmailCommand(
                validated.Value.Id,
                command.ExpectedVersion,
                command.Email),
            cancellationToken);
        return ToAccountResult(changed);
    }

    public async Task<OperationResult<HelloAccount<TProfile>>>
        ChangePhoneAsync(
            HelloChangePhoneCommand command,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validated = await ValidateTokenAsync(
            command.AccessToken,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloAccount<TProfile>>(
                validated.Errors);
        }

        var handleAllowed = await EnsureLocalHandleChangeAllowedAsync(
            validated.Value,
            validated.Value.UserName,
            validated.Value.Email,
            command.Phone,
            cancellationToken);
        if (!handleAllowed.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloAccount<TProfile>>(
                handleAllowed.Errors);
        }

        var changed = await users.ChangePhoneAsync(
            new ChangePhoneCommand(
                validated.Value.Id,
                command.ExpectedVersion,
                command.Phone),
            cancellationToken);
        return ToAccountResult(changed);
    }

    public async Task<OperationResult<HelloAccount<TProfile>>>
        ReplaceProfileAsync(
            HelloReplaceProfileCommand<TProfile> command,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validated = await ValidateTokenAsync(
            command.AccessToken,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloAccount<TProfile>>(
                validated.Errors);
        }

        var changed = await users.PatchProfileAsync(
            new PatchProfileCommand<TProfile>(
                validated.Value.Id,
                command.ExpectedVersion,
                command.Profile),
            cancellationToken);
        return ToAccountResult(changed);
    }

    public async Task<OperationResult<HelloCredentialState>>
        GetCredentialStateAsync(
            string accessToken,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        var validated = await ValidateTokenAsync(
            accessToken,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloCredentialState>(
                validated.Errors);
        }

        if (signInMethods is null)
        {
            return OperationResultFactory.Fail<HelloCredentialState>(
                SignInMethodsUnavailable().Errors);
        }

        var snapshot = await signInMethods.GetAsync(
            validated.Value.Id,
            cancellationToken);
        return snapshot.IsSuccess
            ? OperationResultFactory.Success(
                new HelloCredentialState(
                    snapshot.Value.HasPassword,
                    snapshot.Value.HasPassword
                    && snapshot.Value.ExternalLogins.Count > 0))
            : OperationResultFactory.Fail<HelloCredentialState>(
                snapshot.Errors);
    }

    public async Task<OperationResult<HelloTotpState>> GetTotpStateAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        var validated = await ValidateTokenAsync(
            accessToken,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloTotpState>(
                validated.Errors);
        }

        if (!options.Totp.Enabled || totp is null)
        {
            return OperationResultFactory.Success(
                new HelloTotpState(
                    IsAvailable: false,
                    IsEnabled: false,
                    RecoveryCodesRemaining: 0,
                    EnabledAt: null));
        }

        var status = await totp.GetStatusAsync(
            validated.Value.Id,
            cancellationToken);
        return status.IsSuccess
            ? OperationResultFactory.Success(ToTotpState(status.Value))
            : OperationResultFactory.Fail<HelloTotpState>(status.Errors);
    }

    public async Task<OperationResult<HelloTotpEnrollment>>
        BeginTotpEnrollmentAsync(
            HelloBeginTotpEnrollmentCommand command,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var available = RequireTotpService<HelloTotpEnrollment>();
        if (available is not null)
        {
            return available;
        }

        var validated = await ValidateTokenAsync(
            command.AccessToken,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloTotpEnrollment>(
                validated.Errors);
        }

        var begun = await totp!.BeginEnrollmentAsync(
            new BeginTotpEnrollmentCommand(
                validated.Value.Id,
                command.ClientKey),
            cancellationToken);
        if (!begun.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloTotpEnrollment>(
                begun.Errors);
        }

        var account = FirstNonEmpty(
            validated.Value.Email,
            validated.Value.UserName,
            validated.Value.Phone)
            ?? validated.Value.Id.ToString("D");
        var uri = CreateProvisioningUri(
            options.Totp.Issuer,
            account,
            begun.Value.Secret);
        return OperationResultFactory.Success(
            new HelloTotpEnrollment(
                begun.Value.EnrollmentId,
                begun.Value.Secret,
                uri,
                CreateQrCodeSvg(uri),
                begun.Value.ExpiresAt));
    }

    public async Task<OperationResult<HelloConfirmedTotpEnrollment>>
        ConfirmTotpEnrollmentAsync(
            HelloConfirmTotpEnrollmentCommand command,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var available = RequireTotpService<
            HelloConfirmedTotpEnrollment>();
        if (available is not null)
        {
            return available;
        }

        var validated = await ValidateTokenAsync(
            command.AccessToken,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            return OperationResultFactory.Fail<
                HelloConfirmedTotpEnrollment>(validated.Errors);
        }

        var confirmed = await totp!.ConfirmEnrollmentAsync(
            new ConfirmTotpEnrollmentCommand(
                validated.Value.Id,
                command.EnrollmentId,
                command.Code,
                command.ClientKey),
            cancellationToken);
        return confirmed.IsSuccess
            ? OperationResultFactory.Success(
                new HelloConfirmedTotpEnrollment(
                    ToTotpState(confirmed.Value.Status),
                    confirmed.Value.RecoveryCodes))
            : OperationResultFactory.Fail<
                HelloConfirmedTotpEnrollment>(confirmed.Errors);
    }

    public Task<OperationResult<HelloStepUpChallenge>> BeginTotpDisableAsync(
        HelloBeginTotpDisableCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!options.Totp.Enabled || totp is null)
        {
            return Task.FromResult(
                OperationResultFactory.Fail<HelloStepUpChallenge>(
                    TotpUnavailable()));
        }

        return BeginAccountSecurityActionAsync(
            command.AccessToken,
            command.ClientKey,
            HelloAccountSecurity.AuthenticatorDisableAction,
            RequireTotpEnabledAsync,
            cancellationToken);
    }

    public Task<OperationResult> CompleteTotpDisableAsync(
        HelloCompleteTotpDisableCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!options.Totp.Enabled || totp is null)
        {
            return Task.FromResult(
                OperationResultFactory.Fail(TotpUnavailable()));
        }

        return CompleteAccountSecurityActionAsync(
            command.AccessToken,
            command.ChallengeId,
            command.VerificationCode,
            command.ClientKey,
            HelloAccountSecurity.AuthenticatorDisableAction,
            async (user, ct) =>
            {
                var enabled = await RequireTotpEnabledAsync(user, ct);
                return enabled.IsSuccess
                    ? await totp.DisableAsync(user.Id, ct)
                    : enabled;
            },
            cancellationToken);
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

        var validated = await ValidateTokenAsync(
            command.AccessToken,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                validated.Errors);
        }

        var user = validated.Value;
        var selected = await stepUpMethods.SelectAsync(
            user,
            cancellationToken);
        if (!selected.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                selected.Errors);
        }

        var issued = await stepUp.BeginAsync(
            new BeginStepUpCommand(
                user.Id,
                HelloAccountSecurity.PasswordChangeAction,
                HelloAccountSecurity.CreateBinding(
                    user.Id,
                    selected.Value,
                    HelloAccountSecurity.PasswordChangeAction),
                selected.Value.Method,
                command.ClientKey),
            cancellationToken);
        if (!issued.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                issued.Errors);
        }

        var delivered = await DeliverStepUpAsync(
            HelloAccountMessageKind.PasswordChangeVerification,
            selected.Value,
            issued.Value,
            null,
            cancellationToken);
        return delivered.IsSuccess
            ? OperationResultFactory.Success(
                new HelloStepUpChallenge(
                    issued.Value.ChallengeId,
                    issued.Value.ExpiresAt,
                    selected.Value.Channel))
            : OperationResultFactory.Fail<HelloStepUpChallenge>(
                delivered.Errors);
    }

    public async Task<OperationResult> CompletePasswordChangeAsync(
        HelloCompletePasswordChangeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validated = await ValidateTokenAsync(
            command.AccessToken,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            return OperationResultFactory.Fail(validated.Errors);
        }

        var user = validated.Value;
        var verified = await verification.VerifyAsync(
            new VerifyVerificationChallengeCommand(
                command.ChallengeId,
                user.Id,
                command.VerificationCode,
                command.ClientKey),
            cancellationToken);
        if (!verified.IsSuccess)
        {
            return HelloAccountSecurity.IsRetryableVerificationResponse(
                verified.Errors)
                ? OperationResultFactory.Fail(verified.Errors)
                : PasswordChangeRestartRequired(verified.Errors);
        }

        var selected = stepUpMethods.Resolve(
            user,
            verified.Value.Method,
            requireDelivery: false);
        if (!selected.IsSuccess)
        {
            return PasswordChangeRestartRequired(selected.Errors);
        }

        var authorized = await stepUp.AuthorizeAsync(
            new AuthorizeStepUpCommand(
                user.Id,
                HelloAccountSecurity.PasswordChangeAction,
                HelloAccountSecurity.CreateBinding(
                    user.Id,
                    selected.Value,
                    HelloAccountSecurity.PasswordChangeAction),
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

    public Task<OperationResult<HelloStepUpChallenge>> BeginPasswordSetAsync(
        HelloBeginPasswordSetCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return BeginAccountSecurityActionAsync(
            command.AccessToken,
            command.ClientKey,
            HelloAccountSecurity.PasswordSetAction,
            RequirePasswordAbsentAsync,
            cancellationToken);
    }

    public Task<OperationResult> CompletePasswordSetAsync(
        HelloCompletePasswordSetCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return CompleteAccountSecurityActionAsync(
            command.AccessToken,
            command.ChallengeId,
            command.VerificationCode,
            command.ClientKey,
            HelloAccountSecurity.PasswordSetAction,
            async (user, ct) =>
            {
                var allowed = await RequirePasswordAbsentAsync(user, ct);
                return allowed.IsSuccess
                    ? await credentials.SetPasswordAsync(
                        new SetPasswordCommand(
                            user.Id,
                            user.Version,
                            command.NewPassword),
                        ct)
                    : allowed;
            },
            cancellationToken);
    }

    public Task<OperationResult<HelloStepUpChallenge>>
        BeginPasswordRemovalAsync(
            HelloBeginPasswordRemovalCommand command,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return BeginAccountSecurityActionAsync(
            command.AccessToken,
            command.ClientKey,
            HelloAccountSecurity.PasswordRemoveAction,
            RequirePasswordRemovableAsync,
            cancellationToken);
    }

    public Task<OperationResult> CompletePasswordRemovalAsync(
        HelloCompletePasswordRemovalCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return CompleteAccountSecurityActionAsync(
            command.AccessToken,
            command.ChallengeId,
            command.VerificationCode,
            command.ClientKey,
            HelloAccountSecurity.PasswordRemoveAction,
            async (user, ct) =>
            {
                var allowed = await RequirePasswordRemovableAsync(user, ct);
                return allowed.IsSuccess
                    ? await credentials.RemovePasswordAsync(
                        new RemovePasswordCommand(
                            user.Id,
                            user.Version),
                        ct)
                    : allowed;
            },
            cancellationToken);
    }

    public Task<OperationResult<HelloStepUpChallenge>>
        BeginAccountDeletionAsync(
            HelloBeginAccountDeletionCommand command,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return BeginAccountSecurityActionAsync(
            command.AccessToken,
            command.ClientKey,
            HelloAccountSecurity.AccountDeleteAction,
            precondition: null,
            cancellationToken);
    }

    public Task<OperationResult> CompleteAccountDeletionAsync(
        HelloCompleteAccountDeletionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return CompleteAccountSecurityActionAsync(
            command.AccessToken,
            command.ChallengeId,
            command.VerificationCode,
            command.ClientKey,
            HelloAccountSecurity.AccountDeleteAction,
            (user, ct) => users.DeleteAsync(
                new DeleteUserCommand(
                    user.Id,
                    user.Version,
                    "Self-service account deletion."),
                ct),
            cancellationToken);
    }

    private async Task<OperationResult<HelloStepUpChallenge>>
        BeginAccountSecurityActionAsync(
            string accessToken,
            string? clientKey,
            string action,
            Func<IdentityUser<TProfile>, CancellationToken,
                Task<OperationResult>>? precondition,
            CancellationToken cancellationToken)
    {
        var validated = await ValidateTokenAsync(
            accessToken,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                validated.Errors);
        }

        var user = validated.Value;
        if (precondition is not null)
        {
            var allowed = await precondition(user, cancellationToken);
            if (!allowed.IsSuccess)
            {
                return OperationResultFactory.Fail<HelloStepUpChallenge>(
                    allowed.Errors);
            }
        }

        var selected = await stepUpMethods.SelectAsync(
            user,
            cancellationToken);
        if (!selected.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                selected.Errors);
        }

        var issued = await stepUp.BeginAsync(
            new BeginStepUpCommand(
                user.Id,
                action,
                HelloAccountSecurity.CreateBinding(
                    user.Id,
                    selected.Value,
                    action),
                selected.Value.Method,
                clientKey),
            cancellationToken);
        if (!issued.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                issued.Errors);
        }

        var delivered = await DeliverStepUpAsync(
            HelloAccountMessageKind.AccountSecurityVerification,
            selected.Value,
            issued.Value,
            action,
            cancellationToken);
        return delivered.IsSuccess
            ? OperationResultFactory.Success(
                new HelloStepUpChallenge(
                    issued.Value.ChallengeId,
                    issued.Value.ExpiresAt,
                    selected.Value.Channel))
            : OperationResultFactory.Fail<HelloStepUpChallenge>(
                delivered.Errors);
    }

    private async Task<OperationResult> CompleteAccountSecurityActionAsync(
        string accessToken,
        Guid challengeId,
        string verificationCode,
        string? clientKey,
        string action,
        Func<IdentityUser<TProfile>, CancellationToken,
            Task<OperationResult>> mutation,
        CancellationToken cancellationToken)
    {
        var validated = await ValidateTokenAsync(
            accessToken,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            return OperationResultFactory.Fail(validated.Errors);
        }

        var user = validated.Value;
        var verified = await verification.VerifyAsync(
            new VerifyVerificationChallengeCommand(
                challengeId,
                user.Id,
                verificationCode,
                clientKey),
            cancellationToken);
        if (!verified.IsSuccess)
        {
            return HelloAccountSecurity.IsRetryableVerificationResponse(
                verified.Errors)
                ? OperationResultFactory.Fail(verified.Errors)
                : AccountSecurityActionRestartRequired(verified.Errors);
        }

        var selected = stepUpMethods.Resolve(
            user,
            verified.Value.Method,
            requireDelivery: false);
        if (!selected.IsSuccess)
        {
            return AccountSecurityActionRestartRequired(selected.Errors);
        }

        var authorized = await stepUp.AuthorizeAsync(
            new AuthorizeStepUpCommand(
                user.Id,
                action,
                HelloAccountSecurity.CreateBinding(
                    user.Id,
                    selected.Value,
                    action),
                challengeId,
                verified.Value.Token),
            cancellationToken);
        if (!authorized.IsSuccess)
        {
            return AccountSecurityActionRestartRequired(
                authorized.Errors);
        }

        var mutated = await mutation(user, cancellationToken);
        if (!mutated.IsSuccess)
        {
            return AccountSecurityActionRestartRequired(mutated.Errors);
        }

        var revoked = await sessions.RevokeAllAsync(
            new RevokeAllIdentitySessionsCommand(user.Id),
            cancellationToken);
        return revoked.IsSuccess
            ? revoked
            : AccountSecurityActionSessionCleanupRequired(
                revoked.Errors);
    }

    private async Task<OperationResult> DeliverStepUpAsync(
        HelloAccountMessageKind kind,
        HelloStepUpMethodSelection selection,
        IssuedVerificationChallenge issued,
        string? templateVariant,
        CancellationToken cancellationToken)
    {
        if (string.Equals(
                selection.Method,
                VerificationMethods.TimeBasedOneTimePassword,
                StringComparison.Ordinal))
        {
            return string.IsNullOrWhiteSpace(issued.DeliveryCode)
                ? OperationResultFactory.Success()
                : OperationResultFactory.Fail(
                    new Error(
                        IdentityErrorCodes.VerificationMethodUnavailable,
                        "The authenticator method unexpectedly produced a delivery code.",
                        ErrorType.Failure));
        }

        if (string.IsNullOrWhiteSpace(issued.DeliveryCode)
            || string.IsNullOrWhiteSpace(selection.Destination))
        {
            return OperationResultFactory.Fail(
                new Error(
                    IdentityErrorCodes.VerificationMethodUnavailable,
                    "The verification code could not be delivered.",
                    ErrorType.Failure));
        }

        return await messageSender.SendAsync(
            new HelloAccountMessage(
                Guid.NewGuid(),
                kind,
                selection.Channel,
                selection.Destination,
                ActionUrl: null,
                issued.ExpiresAt,
                issued.DeliveryCode,
                templateVariant),
            cancellationToken);
    }

    private async Task<OperationResult> RequirePasswordAbsentAsync(
        IdentityUser<TProfile> user,
        CancellationToken cancellationToken)
    {
        if (signInMethods is null)
        {
            return SignInMethodsUnavailable();
        }

        var snapshot = await signInMethods.GetAsync(
            user.Id,
            cancellationToken);
        if (!snapshot.IsSuccess)
        {
            return OperationResultFactory.Fail(snapshot.Errors);
        }

        if (!HasLocalHandle(user))
        {
            return OperationResultFactory.Fail(
                new Error(
                    HelloAccountSecurityActionErrorCodes
                        .PasswordLoginHandleRequired,
                    "Add a user name, email address or phone number before setting a password.",
                    ErrorType.Conflict));
        }

        return snapshot.Value.HasPassword
            ? OperationResultFactory.Fail(
                PasswordCredentialErrors.AlreadySet())
            : OperationResultFactory.Success();
    }

    private async Task<OperationResult> RequirePasswordRemovableAsync(
        IdentityUser<TProfile> user,
        CancellationToken cancellationToken)
    {
        if (signInMethods is null)
        {
            return SignInMethodsUnavailable();
        }

        var snapshot = await signInMethods.GetAsync(
            user.Id,
            cancellationToken);
        if (!snapshot.IsSuccess)
        {
            return OperationResultFactory.Fail(snapshot.Errors);
        }

        if (!snapshot.Value.HasPassword)
        {
            return OperationResultFactory.Fail(
                PasswordCredentialErrors.NotSet());
        }

        return snapshot.Value.ExternalLogins.Count > 0
            ? OperationResultFactory.Success()
            : OperationResultFactory.Fail(
                new Error(
                    HelloAccountSecurityActionErrorCodes.LastSignInMethod,
                    "A password cannot be removed until another sign-in method is linked.",
                    ErrorType.Conflict));
    }

    private async Task<OperationResult>
        EnsureLocalHandleChangeAllowedAsync(
            IdentityUser<TProfile> user,
            string? userName,
            string? email,
            string? phone,
            CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(userName)
            || !string.IsNullOrWhiteSpace(email)
            || !string.IsNullOrWhiteSpace(phone))
        {
            return OperationResultFactory.Success();
        }

        if (signInMethods is null)
        {
            return SignInMethodsUnavailable();
        }

        var snapshot = await signInMethods.GetAsync(
            user.Id,
            cancellationToken);
        if (!snapshot.IsSuccess)
        {
            return OperationResultFactory.Fail(snapshot.Errors);
        }

        return snapshot.Value.HasPassword
            || snapshot.Value.ExternalLogins.Count == 0
                ? OperationResultFactory.Fail(
                    new Error(
                        HelloAccountSecurityActionErrorCodes
                            .LastSignInMethod,
                        "At least one user name, email address or phone number must remain while password sign-in is configured.",
                        ErrorType.Conflict))
                : OperationResultFactory.Success();
    }

    private async Task<OperationResult> RequireTotpEnabledAsync(
        IdentityUser<TProfile> user,
        CancellationToken cancellationToken)
    {
        if (!options.Totp.Enabled || totp is null)
        {
            return OperationResultFactory.Fail(TotpUnavailable());
        }

        var status = await totp.GetStatusAsync(
            user.Id,
            cancellationToken);
        if (!status.IsSuccess)
        {
            return OperationResultFactory.Fail(status.Errors);
        }

        return status.Value.IsEnabled
            ? OperationResultFactory.Success()
            : OperationResultFactory.Fail(
                new Error(
                    IdentityErrorCodes.TotpNotEnabled,
                    "An authenticator is not enabled.",
                    ErrorType.Conflict));
    }

    private OperationResult<T>? RequireTotpService<T>()
        => options.Totp.Enabled && totp is not null
            ? null
            : OperationResultFactory.Fail<T>(TotpUnavailable());

    private static Error TotpUnavailable()
        => new(
            IdentityErrorCodes.VerificationMethodUnavailable,
            "Authenticator support is not configured.",
            ErrorType.Failure);

    private static HelloTotpState ToTotpState(TotpFactorStatus status)
        => new(
            IsAvailable: true,
            status.IsEnabled,
            status.RecoveryCodesRemaining,
            status.EnabledAt);

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?
            .Trim();

    internal static string CreateProvisioningUri(
        string issuer,
        string account,
        string secret)
    {
        var escapedIssuer = Uri.EscapeDataString(issuer);
        var escapedLabel = $"{Uri.EscapeDataString(issuer)}:"
            + Uri.EscapeDataString(account);
        return $"otpauth://totp/{escapedLabel}?secret={secret}"
            + $"&issuer={escapedIssuer}";
    }

    private static string CreateQrCodeSvg(string provisioningUri)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(
            provisioningUri,
            QRCodeGenerator.ECCLevel.M);
        var qrCode = new SvgQRCode(data);
        return qrCode.GetGraphic(4);
    }

    private static bool HasLocalHandle(IdentityUser<TProfile> user)
        => !string.IsNullOrWhiteSpace(user.UserName)
            || !string.IsNullOrWhiteSpace(user.Email)
            || !string.IsNullOrWhiteSpace(user.Phone);

    private static OperationResult SignInMethodsUnavailable()
        => OperationResultFactory.Fail(
            new Error(
                "hello.account.sign_in_methods_unavailable",
                "Sign-in method information is unavailable.",
                ErrorType.Failure));

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

    private static OperationResult AccountSecurityActionRestartRequired(
        IReadOnlyCollection<Error> causes)
        => OperationResultFactory.Fail(
            causes.Prepend(
                    new Error(
                        HelloAccountSecurityActionErrorCodes.RestartRequired,
                        "The verification code can no longer be used. Request a new code and try again.",
                        ErrorType.Conflict))
                .ToArray());

    private static OperationResult
        AccountSecurityActionSessionCleanupRequired(
            IReadOnlyCollection<Error> causes)
        => OperationResultFactory.Fail(
            causes.Prepend(
                    new Error(
                        HelloAccountSecurityActionErrorCodes
                            .SessionCleanupRequired,
                        "The account security action completed, but session cleanup could not be completed. Sign in again.",
                        ErrorType.Conflict))
                .ToArray());

    private async Task<OperationResult<IdentityUser<TProfile>>>
        ValidateTokenAsync(
            string accessToken,
            CancellationToken cancellationToken)
    {
        OperationResult<IdentityUser<TProfile>>? firstFailure = null;
        if (accessTokenValidators is not null)
        {
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
        }

        return firstFailure
            ?? await sessions.ValidateAccessTokenAsync(
                accessToken,
                cancellationToken);
    }

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

    private static OperationResult<HelloAccount<TProfile>> ToAccountResult(
        OperationResult<IdentityUser<TProfile>> result)
        => result.IsSuccess
            ? OperationResultFactory.Success(ToAccount(result.Value))
            : OperationResultFactory.Fail<HelloAccount<TProfile>>(
                result.Errors);

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
