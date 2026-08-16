using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;

namespace Skopka.Hello.UI;

internal sealed class HelloUiApplication<TProfile>(
    IHelloIdentityApplication<TProfile> application,
    IHelloUiProfileFactory<TProfile> profiles,
    IHelloRequestContext requestContext,
    SkopkaHelloOptions helloOptions,
    IHelloRegistrationConsentPolicy registrationConsentPolicy,
    TimeProvider timeProvider,
    IHelloUiProfileEditor<TProfile>? profileEditor = null)
    : IHelloUiApplication
{
    public async Task<OperationResult> RegisterAsync(
        HelloUiRegisterCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!helloOptions.SelfRegistrationEnabled)
        {
            return OperationResultFactory.Fail(
                HelloRegistrationErrors.Disabled());
        }

        var submittedConsent = CreateRegistrationConsent(
            command.AcceptTermsOfService,
            command.AcceptPrivacyPolicy);
        var consentValidation = registrationConsentPolicy.Validate(
            submittedConsent);
        if (!consentValidation.IsSuccess)
        {
            return OperationResultFactory.Fail(
                consentValidation.Errors);
        }
        var registrationConsent = consentValidation.Value;

        var profile = profiles.Create(
            new HelloUiRegistrationProfile(
                command.DisplayName,
                command.Locale)
            {
                RegistrationConsent = registrationConsent,
            });
        if (!profile.IsSuccess)
        {
            return OperationResultFactory.Fail(profile.Errors);
        }

        var result = await application.RegisterAsync(
            new HelloRegisterCommand<TProfile>(
                command.UserName,
                command.Email,
                command.Phone,
                profile.Value,
                command.Password)
            {
                RegistrationConsent = registrationConsent,
            },
            cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success()
            : OperationResultFactory.Fail(result.Errors);
    }

    private HelloRegistrationConsent CreateRegistrationConsent(
        bool termsOfService,
        bool privacyPolicy)
        => new(
            termsOfService,
            privacyPolicy,
            termsOfService || privacyPolicy
                ? timeProvider.GetUtcNow()
                : null);

    public async Task<OperationResult<HelloUiSignIn>> LoginAsync(
        HelloUiLoginCommand command,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(httpContext);

        var result = await application.LoginAsync(
            new HelloLoginCommand(
                command.Login,
                command.Password,
                requestContext.CreateClientKey(httpContext),
                requestContext.CreateSessionMetadata(
                    httpContext,
                    helloOptions.ClientName)),
            cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success(
                new HelloUiSignIn(
                    HelloUiPrincipalFactory.Create(
                        result.Value.Account,
                        result.Value.Session.SessionId,
                        profiles),
                    result.Value.Session))
            : OperationResultFactory.Fail<HelloUiSignIn>(
                result.Errors);
    }

    public async Task<OperationResult<HelloUiAccount>> GetAccountAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var validated = await ValidateAccountAsync(
            httpContext,
            cancellationToken);
        return validated.IsSuccess
            ? OperationResultFactory.Success(ToUiAccount(validated.Value))
            : OperationResultFactory.Fail<HelloUiAccount>(
                validated.Errors);
    }

    public async Task<OperationResult<HelloUiAccount>> ChangeUserNameAsync(
        HelloUiChangeUserNameCommand command,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var accessToken = await ReadAccessTokenAsync(httpContext);
        if (accessToken is null)
        {
            return InvalidSession<HelloUiAccount>();
        }

        var changed = await application.ChangeUserNameAsync(
            new HelloChangeUserNameCommand(
                accessToken,
                command.ExpectedVersion,
                command.UserName),
            cancellationToken);
        return ToUiAccountResult(changed);
    }

    public async Task<OperationResult<HelloUiAccount>> ChangeEmailAsync(
        HelloUiChangeEmailCommand command,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var accessToken = await ReadAccessTokenAsync(httpContext);
        if (accessToken is null)
        {
            return InvalidSession<HelloUiAccount>();
        }

        var changed = await application.ChangeEmailAsync(
            new HelloChangeEmailCommand(
                accessToken,
                command.ExpectedVersion,
                NormalizeOptional(command.Email)),
            cancellationToken);
        return ToUiAccountResult(changed);
    }

    public async Task<OperationResult<HelloUiAccount>> ChangePhoneAsync(
        HelloUiChangePhoneCommand command,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var accessToken = await ReadAccessTokenAsync(httpContext);
        if (accessToken is null)
        {
            return InvalidSession<HelloUiAccount>();
        }

        var changed = await application.ChangePhoneAsync(
            new HelloChangePhoneCommand(
                accessToken,
                command.ExpectedVersion,
                NormalizeOptional(command.Phone)),
            cancellationToken);
        return ToUiAccountResult(changed);
    }

    public async Task<OperationResult<HelloUiAccount>> UpdateProfileAsync(
        HelloUiUpdateProfileCommand command,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (profileEditor is null)
        {
            return ProfileEditingUnavailable();
        }

        var validated = await ValidateAccountAsync(
            httpContext,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloUiAccount>(
                validated.Errors);
        }

        var profile = profileEditor.Update(
            validated.Value.Profile,
            command.Values);
        if (!profile.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloUiAccount>(
                profile.Errors);
        }

        var accessToken = await ReadAccessTokenAsync(httpContext);
        if (accessToken is null)
        {
            return InvalidSession<HelloUiAccount>();
        }

        var changed = await application.ReplaceProfileAsync(
            new HelloReplaceProfileCommand<TProfile>(
                accessToken,
                command.ExpectedVersion,
                profile.Value),
            cancellationToken);
        return ToUiAccountResult(changed);
    }

    public Task<OperationResult> RequestPasswordResetAsync(
        string email,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => application.RequestPasswordResetAsync(
            email,
            requestContext.CreateClientKey(httpContext),
            cancellationToken);

    public Task<OperationResult> ResetPasswordAsync(
        HelloUiResetPasswordCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return application.ResetPasswordAsync(
            new HelloResetPasswordCommand(
                command.UserId,
                command.Token,
                command.NewPassword),
            cancellationToken);
    }

    public Task<OperationResult> RequestEmailConfirmationAsync(
        string email,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => application.RequestEmailConfirmationAsync(
            email,
            requestContext.CreateClientKey(httpContext),
            cancellationToken);

    public async Task<OperationResult> ConfirmEmailAsync(
        HelloUiConfirmEmailCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await application.ConfirmEmailAsync(
            new HelloConfirmEmailCommand(
                command.UserId,
                command.Email,
                command.Token),
            cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success()
            : OperationResultFactory.Fail(result.Errors);
    }

    public Task<OperationResult> RequestPhoneConfirmationAsync(
        string phone,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => application.RequestPhoneConfirmationAsync(
            phone,
            requestContext.CreateClientKey(httpContext),
            cancellationToken);

    public async Task<OperationResult> ConfirmPhoneAsync(
        HelloUiConfirmPhoneCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await application.ConfirmPhoneAsync(
            new HelloConfirmPhoneCommand(
                command.UserId,
                command.Phone,
                command.Token),
            cancellationToken);
        return result.IsSuccess
            ? OperationResultFactory.Success()
            : OperationResultFactory.Fail(result.Errors);
    }

    public Task<OperationResult<IReadOnlyList<HelloSessionInfo>>>
        ListSessionsAsync(
            Guid userId,
            CancellationToken cancellationToken)
        => application.ListSessionsAsync(
            userId,
            cancellationToken);

    public Task<OperationResult> RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken)
        => application.RevokeSessionAsync(
            userId,
            sessionId,
            cancellationToken);

    public Task<OperationResult> LogoutAsync(
        string refreshToken,
        CancellationToken cancellationToken)
        => application.LogoutAsync(
            refreshToken,
            cancellationToken);

    public Task<OperationResult> LogoutAllAsync(
        Guid userId,
        CancellationToken cancellationToken)
        => application.LogoutAllAsync(
            userId,
            cancellationToken);

    public async Task<OperationResult<HelloStepUpChallenge>>
        BeginPasswordChangeAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var accessToken = await ReadAccessTokenAsync(httpContext);
        if (accessToken is null)
        {
            return InvalidSession<HelloStepUpChallenge>();
        }

        return await application.BeginPasswordChangeAsync(
            new HelloBeginPasswordChangeCommand(
                accessToken,
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
    }

    public async Task<OperationResult> CompletePasswordChangeAsync(
        HelloUiCompletePasswordChangeCommand command,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(httpContext);

        var accessToken = await ReadAccessTokenAsync(httpContext);
        if (accessToken is null)
        {
            return InvalidSession();
        }

        return await application.CompletePasswordChangeAsync(
            new HelloCompletePasswordChangeCommand(
                accessToken,
                command.ChallengeId,
                command.VerificationCode,
                command.CurrentPassword,
                command.NewPassword,
                requestContext.CreateClientKey(httpContext)),
            cancellationToken);
    }

    public async Task<OperationResult<HelloCredentialState>>
        GetCredentialStateAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        var accessToken = await ReadAccessTokenAsync(httpContext);
        return accessToken is null
            ? InvalidSession<HelloCredentialState>()
            : await application.GetCredentialStateAsync(
                accessToken,
                cancellationToken);
    }

    public async Task<OperationResult<HelloTotpState>> GetTotpStateAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var accessToken = await ReadAccessTokenAsync(httpContext);
        return accessToken is null
            ? InvalidSession<HelloTotpState>()
            : await application.GetTotpStateAsync(
                accessToken,
                cancellationToken);
    }

    public async Task<OperationResult<HelloTotpEnrollment>>
        BeginTotpEnrollmentAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        var accessToken = await ReadAccessTokenAsync(httpContext);
        return accessToken is null
            ? InvalidSession<HelloTotpEnrollment>()
            : await application.BeginTotpEnrollmentAsync(
                new HelloBeginTotpEnrollmentCommand(
                    accessToken,
                    requestContext.CreateClientKey(httpContext)),
                cancellationToken);
    }

    public async Task<OperationResult<HelloConfirmedTotpEnrollment>>
        ConfirmTotpEnrollmentAsync(
            HelloUiConfirmTotpEnrollmentCommand command,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var accessToken = await ReadAccessTokenAsync(httpContext);
        return accessToken is null
            ? InvalidSession<HelloConfirmedTotpEnrollment>()
            : await application.ConfirmTotpEnrollmentAsync(
                new HelloConfirmTotpEnrollmentCommand(
                    accessToken,
                    command.EnrollmentId,
                    command.Code,
                    requestContext.CreateClientKey(httpContext)),
                cancellationToken);
    }

    public async Task<OperationResult<HelloStepUpChallenge>>
        BeginTotpDisableAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        var accessToken = await ReadAccessTokenAsync(httpContext);
        return accessToken is null
            ? InvalidSession<HelloStepUpChallenge>()
            : await application.BeginTotpDisableAsync(
                new HelloBeginTotpDisableCommand(
                    accessToken,
                    requestContext.CreateClientKey(httpContext)),
                cancellationToken);
    }

    public async Task<OperationResult> CompleteTotpDisableAsync(
        HelloUiCompleteAccountSecurityActionCommand command,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var accessToken = await ReadAccessTokenAsync(httpContext);
        return accessToken is null
            ? InvalidSession()
            : await application.CompleteTotpDisableAsync(
                new HelloCompleteTotpDisableCommand(
                    accessToken,
                    command.ChallengeId,
                    command.VerificationCode,
                    requestContext.CreateClientKey(httpContext)),
                cancellationToken);
    }

    public async Task<OperationResult<HelloStepUpChallenge>>
        BeginPasswordSetAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        var accessToken = await ReadAccessTokenAsync(httpContext);
        return accessToken is null
            ? InvalidSession<HelloStepUpChallenge>()
            : await application.BeginPasswordSetAsync(
                new HelloBeginPasswordSetCommand(
                    accessToken,
                    requestContext.CreateClientKey(httpContext)),
                cancellationToken);
    }

    public async Task<OperationResult> CompletePasswordSetAsync(
        HelloUiCompletePasswordSetCommand command,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var accessToken = await ReadAccessTokenAsync(httpContext);
        return accessToken is null
            ? InvalidSession()
            : await application.CompletePasswordSetAsync(
                new HelloCompletePasswordSetCommand(
                    accessToken,
                    command.ChallengeId,
                    command.VerificationCode,
                    command.NewPassword,
                    requestContext.CreateClientKey(httpContext)),
                cancellationToken);
    }

    public async Task<OperationResult<HelloStepUpChallenge>>
        BeginPasswordRemovalAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        var accessToken = await ReadAccessTokenAsync(httpContext);
        return accessToken is null
            ? InvalidSession<HelloStepUpChallenge>()
            : await application.BeginPasswordRemovalAsync(
                new HelloBeginPasswordRemovalCommand(
                    accessToken,
                    requestContext.CreateClientKey(httpContext)),
                cancellationToken);
    }

    public async Task<OperationResult> CompletePasswordRemovalAsync(
        HelloUiCompleteAccountSecurityActionCommand command,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var accessToken = await ReadAccessTokenAsync(httpContext);
        return accessToken is null
            ? InvalidSession()
            : await application.CompletePasswordRemovalAsync(
                new HelloCompletePasswordRemovalCommand(
                    accessToken,
                    command.ChallengeId,
                    command.VerificationCode,
                    requestContext.CreateClientKey(httpContext)),
                cancellationToken);
    }

    public async Task<OperationResult<HelloStepUpChallenge>>
        BeginAccountDeletionAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        var accessToken = await ReadAccessTokenAsync(httpContext);
        return accessToken is null
            ? InvalidSession<HelloStepUpChallenge>()
            : await application.BeginAccountDeletionAsync(
                new HelloBeginAccountDeletionCommand(
                    accessToken,
                    requestContext.CreateClientKey(httpContext)),
                cancellationToken);
    }

    public async Task<OperationResult> CompleteAccountDeletionAsync(
        HelloUiCompleteAccountSecurityActionCommand command,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var accessToken = await ReadAccessTokenAsync(httpContext);
        return accessToken is null
            ? InvalidSession()
            : await application.CompleteAccountDeletionAsync(
                new HelloCompleteAccountDeletionCommand(
                    accessToken,
                    command.ChallengeId,
                    command.VerificationCode,
                    requestContext.CreateClientKey(httpContext)),
                cancellationToken);
    }

    private async Task<OperationResult<HelloAccount<TProfile>>>
        ValidateAccountAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var accessToken = await ReadAccessTokenAsync(httpContext);
        return accessToken is null
            ? InvalidSession<HelloAccount<TProfile>>()
            : await application.ValidateAccessTokenAsync(
                accessToken,
                cancellationToken);
    }

    private HelloUiAccount ToUiAccount(
        HelloAccount<TProfile> account)
    {
        var displayName = profiles.GetDisplayName(account.Profile);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = account.UserName
                ?? account.Email
                ?? account.Phone
                ?? "Account";
        }

        return new HelloUiAccount(
            account.Id,
            displayName,
            account.UserName,
            account.Email,
            account.EmailConfirmed,
            account.Phone,
            account.PhoneConfirmed,
            account.Version,
            profileEditor?.GetFields(account.Profile) ?? []);
    }

    private OperationResult<HelloUiAccount> ToUiAccountResult(
        OperationResult<HelloAccount<TProfile>> result)
        => result.IsSuccess
            ? OperationResultFactory.Success(ToUiAccount(result.Value))
            : OperationResultFactory.Fail<HelloUiAccount>(result.Errors);

    private static OperationResult<HelloUiAccount>
        ProfileEditingUnavailable()
        => OperationResultFactory.Fail<HelloUiAccount>(
            new Error(
                "hello.ui.profile_editing_unavailable",
                "Profile editing is not configured.",
                ErrorType.Forbidden));

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

    private static OperationResult InvalidSession()
        => OperationResultFactory.Fail(
            new Error(
                IdentityErrorCodes.RefreshTokenInvalid,
                "The session is invalid or expired.",
                ErrorType.Unauthorized));

    private static OperationResult<T> InvalidSession<T>()
        => OperationResultFactory.Fail<T>(
            new Error(
                IdentityErrorCodes.RefreshTokenInvalid,
                "The session is invalid or expired.",
                ErrorType.Unauthorized));

}
