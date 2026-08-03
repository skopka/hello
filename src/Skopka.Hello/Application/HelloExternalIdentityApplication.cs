using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;
using Skopka.Identity.ExternalLogins;
using Skopka.Identity.Registration;
using Skopka.Identity.Sessions;
using Skopka.Identity.SignInMethods;
using Skopka.Identity.StepUp;
using Skopka.Identity.StepUp.Commands;
using Skopka.Identity.Users;
using Skopka.Identity.Users.Commands;
using Skopka.Identity.Verification;

namespace Skopka.Hello;

internal sealed class HelloExternalIdentityApplication<TProfile>(
    IExternalLoginService<TProfile> externalLogins,
    IIdentityRegistrationService<TProfile> registration,
    IIdentitySessionService<TProfile> sessions,
    IIdentitySignInMethodQueryService<TProfile> signInMethods,
    IIdentityStepUpService<TProfile> stepUp,
    IIdentityVerificationService<TProfile> verification,
    IHelloAccountMessageSender messageSender,
    HelloDeliveryOptions deliveryOptions,
    SkopkaHelloOptions options)
    : IHelloExternalIdentityApplication<TProfile>
{
    public async Task<OperationResult<HelloSignIn<TProfile>>> SignInAsync(
        HelloExternalSignInCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var resolved = await externalLogins.ResolveAsync(
            command.Login,
            cancellationToken);
        return resolved.IsSuccess
            ? await CreateSignInAsync(
                resolved.Value,
                command.SessionMetadata,
                cancellationToken)
            : OperationResultFactory.Fail<HelloSignIn<TProfile>>(
                resolved.Errors);
    }

    public async Task<OperationResult<HelloSignIn<TProfile>>> RegisterAsync(
        HelloExternalRegistrationCommand<TProfile> command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!options.SelfRegistrationEnabled)
        {
            return OperationResultFactory.Fail<
                HelloSignIn<TProfile>>(
                    HelloRegistrationErrors.Disabled());
        }

        var registered = await registration.RegisterExternalAsync(
            new RegisterExternalUserCommand<TProfile>(
                new CreateUserCommand<TProfile>(
                    command.UserName,
                    command.Email,
                    command.Phone,
                    command.Profile),
                command.Login),
            cancellationToken);
        return registered.IsSuccess
            ? await CreateSignInAsync(
                registered.Value,
                command.SessionMetadata,
                cancellationToken)
            : OperationResultFactory.Fail<HelloSignIn<TProfile>>(
                registered.Errors);
    }

    public async Task<OperationResult<SignInMethodSnapshot>>
        GetSignInMethodsAsync(
            string accessToken,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        var validated = await sessions.ValidateAccessTokenAsync(
            accessToken,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            return OperationResultFactory.Fail<SignInMethodSnapshot>(
                validated.Errors);
        }

        return await signInMethods.GetAsync(
            validated.Value.Id,
            cancellationToken);
    }

    public Task<OperationResult<HelloStepUpChallenge>> BeginLinkAsync(
        HelloBeginExternalLoginMutationCommand command,
        CancellationToken cancellationToken)
        => BeginMutationAsync(
            command,
            HelloAccountSecurity.ExternalLinkAction,
            cancellationToken);

    public Task<OperationResult<HelloSignIn<TProfile>>> CompleteLinkAsync(
        HelloCompleteExternalLoginMutationCommand command,
        CancellationToken cancellationToken)
        => CompleteMutationAsync(
            command,
            HelloAccountSecurity.ExternalLinkAction,
            link: true,
            cancellationToken);

    public Task<OperationResult<HelloStepUpChallenge>> BeginUnlinkAsync(
        HelloBeginExternalLoginMutationCommand command,
        CancellationToken cancellationToken)
        => BeginMutationAsync(
            command,
            HelloAccountSecurity.ExternalUnlinkAction,
            cancellationToken);

    public Task<OperationResult<HelloSignIn<TProfile>>> CompleteUnlinkAsync(
        HelloCompleteExternalLoginMutationCommand command,
        CancellationToken cancellationToken)
        => CompleteMutationAsync(
            command,
            HelloAccountSecurity.ExternalUnlinkAction,
            link: false,
            cancellationToken);

    private async Task<OperationResult<HelloStepUpChallenge>>
        BeginMutationAsync(
            HelloBeginExternalLoginMutationCommand command,
            string action,
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
                action,
                HelloAccountSecurity.CreateExternalLoginBinding(
                    command.Login,
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
                action switch
                {
                    HelloAccountSecurity.ExternalLinkAction =>
                        HelloAccountMessageKind.ExternalLoginLinkVerification,
                    HelloAccountSecurity.ExternalUnlinkAction =>
                        HelloAccountMessageKind.ExternalLoginUnlinkVerification,
                    _ => throw new InvalidOperationException(
                        "The external account operation is unsupported."),
                },
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

    private async Task<OperationResult<HelloSignIn<TProfile>>>
        CompleteMutationAsync(
            HelloCompleteExternalLoginMutationCommand command,
            string action,
            bool link,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validated = await sessions.ValidateAccessTokenAsync(
            command.AccessToken,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloSignIn<TProfile>>(
                validated.Errors);
        }

        var user = validated.Value;
        if (!HelloAccountSecurity.TryGetConfirmedDestination(
                user,
                deliveryOptions.VerificationChannel,
                out var destination))
        {
            return OperationResultFactory.Fail<HelloSignIn<TProfile>>(
                HelloAccountSecurity.ConfirmedDestinationRequired(
                    deliveryOptions.VerificationChannel));
        }

        if (user.Version != command.ExpectedVersion)
        {
            return OperationResultFactory.Fail<HelloSignIn<TProfile>>(
                HelloAccountSecurity.ConcurrencyConflict());
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
                ? OperationResultFactory.Fail<HelloSignIn<TProfile>>(
                    verified.Errors)
                : ChallengeRestartRequired<TProfile>(verified.Errors);
        }

        var authorized = await stepUp.AuthorizeAsync(
            new AuthorizeStepUpCommand(
                user.Id,
                action,
                HelloAccountSecurity.CreateExternalLoginBinding(
                    command.Login,
                    user.Id,
                    deliveryOptions.VerificationChannel,
                    destination!),
                command.ChallengeId,
                verified.Value.Token),
            cancellationToken);
        if (!authorized.IsSuccess)
        {
            return ChallengeRestartRequired<TProfile>(authorized.Errors);
        }

        var mutated = link
            ? await externalLogins.LinkAsync(
                new LinkExternalLoginCommand(
                    user.Id,
                    command.ExpectedVersion,
                    command.Login),
                cancellationToken)
            : await externalLogins.UnlinkAsync(
                new UnlinkExternalLoginCommand(
                    user.Id,
                    command.ExpectedVersion,
                    command.Login),
                cancellationToken);
        if (!mutated.IsSuccess)
        {
            return RestartRequired<TProfile>(mutated.Errors);
        }

        var revoked = await sessions.RevokeAllAsync(
            new RevokeAllIdentitySessionsCommand(user.Id),
            cancellationToken);
        if (!revoked.IsSuccess)
        {
            return RestartRequired<TProfile>(revoked.Errors);
        }

        var signedIn = await CreateSignInAsync(
            mutated.Value,
            command.SessionMetadata,
            cancellationToken);
        return signedIn.IsSuccess
            ? signedIn
            : RestartRequired<TProfile>(signedIn.Errors);
    }

    private async Task<OperationResult<HelloSignIn<TProfile>>>
        CreateSignInAsync(
            IdentityUser<TProfile> user,
            IdentitySessionMetadata metadata,
            CancellationToken cancellationToken)
    {
        var issued = await sessions.CreateAsync(
            new CreateIdentitySessionCommand(
                user.Id,
                user.SecurityStamp,
                metadata),
            cancellationToken);
        return issued.IsSuccess
            ? OperationResultFactory.Success(
                new HelloSignIn<TProfile>(
                    ToAccount(user),
                    ToSession(issued.Value)))
            : OperationResultFactory.Fail<HelloSignIn<TProfile>>(
                issued.Errors);
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

    private static HelloSession ToSession(IssuedIdentitySession session)
        => new(
            session.SessionId,
            session.AccessToken,
            session.AccessTokenExpiresAt,
            session.RefreshToken,
            session.RefreshTokenExpiresAt);

    private static OperationResult<HelloSignIn<T>> RestartRequired<T>(
        IReadOnlyCollection<Error> causes)
        => OperationResultFactory.Fail<HelloSignIn<T>>(
            causes.Prepend(
                    new Error(
                        HelloExternalIdentityErrorCodes.RestartRequired,
                        "The account change could not be finalized. Refresh the account and start the operation again.",
                        ErrorType.Conflict))
                .ToArray());

    private static OperationResult<HelloSignIn<T>>
        ChallengeRestartRequired<T>(
            IReadOnlyCollection<Error> causes)
        => OperationResultFactory.Fail<HelloSignIn<T>>(
            causes.Prepend(
                    new Error(
                        HelloExternalIdentityErrorCodes
                            .ChallengeRestartRequired,
                        "The verification challenge can no longer be used. Start the account change again.",
                        ErrorType.Conflict))
                .ToArray());
}
