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
    SkopkaHelloOptions options,
    HelloRegistrationAdmission<TProfile>? registrationAdmission = null,
    IEnumerable<IHelloAccessTokenValidator<TProfile>>?
        accessTokenValidators = null,
    HelloStepUpMethodResolver<TProfile>? stepUpMethodResolver = null)
    : IHelloExternalIdentityApplication<TProfile>
{
    private readonly HelloStepUpMethodResolver<TProfile> stepUpMethods =
        stepUpMethodResolver
        ?? new HelloStepUpMethodResolver<TProfile>(
            deliveryOptions,
            messageSender);

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

        if (registrationAdmission is not null)
        {
            var admitted = await registrationAdmission.CheckAsync(
                HelloRegistrationKind.External,
                cancellationToken);
            if (!admitted.IsSuccess)
            {
                return OperationResultFactory.Fail<
                    HelloSignIn<TProfile>>(admitted.Errors);
            }
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

        var validated = await ValidateTokenAsync(
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
                action,
                HelloAccountSecurity.CreateExternalLoginBinding(
                    command.Login,
                    user.Id,
                    selected.Value),
                selected.Value.Method,
                command.ClientKey),
            cancellationToken);
        if (!issued.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                issued.Errors);
        }

        var delivered = await DeliverStepUpAsync(
            action switch
            {
                HelloAccountSecurity.ExternalLinkAction =>
                    HelloAccountMessageKind.ExternalLoginLinkVerification,
                HelloAccountSecurity.ExternalUnlinkAction =>
                    HelloAccountMessageKind.ExternalLoginUnlinkVerification,
                _ => throw new InvalidOperationException(
                    "The external account operation is unsupported."),
            },
            selected.Value,
            issued.Value,
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

    private async Task<OperationResult<HelloSignIn<TProfile>>>
        CompleteMutationAsync(
            HelloCompleteExternalLoginMutationCommand command,
            string action,
            bool link,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validated = await ValidateTokenAsync(
            command.AccessToken,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloSignIn<TProfile>>(
                validated.Errors);
        }

        var user = validated.Value;
        if (user.Version != command.ExpectedVersion)
        {
            return OperationResultFactory.Fail<HelloSignIn<TProfile>>(
                HelloAccountSecurity.ConcurrencyConflict());
        }

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
                ? OperationResultFactory.Fail<HelloSignIn<TProfile>>(
                    verified.Errors)
                : ChallengeRestartRequired<TProfile>(verified.Errors);
        }

        var selected = stepUpMethods.Resolve(
            user,
            verified.Value.Method,
            requireDelivery: false);
        if (!selected.IsSuccess)
        {
            return ChallengeRestartRequired<TProfile>(selected.Errors);
        }

        var authorized = await stepUp.AuthorizeAsync(
            new AuthorizeStepUpCommand(
                user.Id,
                action,
                HelloAccountSecurity.CreateExternalLoginBinding(
                    command.Login,
                    user.Id,
                    selected.Value),
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

    private async Task<OperationResult> DeliverStepUpAsync(
        HelloAccountMessageKind kind,
        HelloStepUpMethodSelection selection,
        IssuedVerificationChallenge issued,
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
                null,
                issued.ExpiresAt,
                issued.DeliveryCode),
            cancellationToken);
    }

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
