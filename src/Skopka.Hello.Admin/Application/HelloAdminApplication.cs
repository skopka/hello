using Skopka.Abstraction.OperationResult;
using Skopka.Identity;
using Skopka.Identity.Errors;
using Skopka.Identity.Sessions;
using Skopka.Identity.StepUp;
using Skopka.Identity.StepUp.Commands;
using Skopka.Identity.Users;
using Skopka.Identity.Users.Commands;
using Skopka.Identity.Users.Queries;
using Skopka.Identity.Verification;

namespace Skopka.Hello.Admin;

internal sealed class HelloAdminApplication<TProfile>(
    IIdentityUserQueryService<TProfile> userQueries,
    IIdentityUserService<TProfile> users,
    IIdentitySessionService<TProfile> sessions,
    IIdentityStepUpService<TProfile> stepUp,
    IIdentityVerificationService<TProfile> verification,
    IHelloAdminProfileProjector<TProfile> profiles,
    IHelloAccountMessageSender messageSender,
    HelloDeliveryOptions deliveryOptions)
    : IHelloAdminApplication
{
    public async Task<OperationResult<HelloAdminUserPage>> QueryUsersAsync(
        HelloAdminQueryUsersCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var actor = await ValidateActorAsync(
            command.AccessToken,
            cancellationToken);
        if (!actor.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloAdminUserPage>(
                actor.Errors);
        }

        var queried = await userQueries.QueryAsync(
            new IdentityUserQuery(
                command.Search,
                command.Status,
                command.RequiredFlags,
                command.PageSize,
                command.Cursor),
            cancellationToken);
        if (!queried.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloAdminUserPage>(
                queried.Errors);
        }

        List<HelloAdminUser> projected = [];
        foreach (var user in queried.Value.Items)
        {
            var item = await ProjectAsync(
                actor.Value.Id,
                user,
                cancellationToken);
            if (!item.IsSuccess)
            {
                return OperationResultFactory.Fail<HelloAdminUserPage>(
                    item.Errors);
            }

            projected.Add(item.Value);
        }

        return OperationResultFactory.Success(
            new HelloAdminUserPage(
                projected,
                queried.Value.NextCursor));
    }

    public async Task<OperationResult<HelloStepUpChallenge>>
        BeginUserActionAsync(
            HelloAdminBeginUserActionCommand command,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Parameters);

        var error = HelloAdminSecurity.Validate(
            command.TargetUserId,
            command.Action,
            command.Parameters);
        if (error is not null)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(error);
        }

        var actor = await ValidateActorAsync(
            command.AccessToken,
            cancellationToken);
        if (!actor.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                actor.Errors);
        }

        if (IsForbiddenSelfMutation(
                actor.Value.Id,
                command.TargetUserId,
                command.Action))
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                HelloAdminSecurity.SelfMutationForbidden());
        }

        if (!TryGetConfirmedDestination(actor.Value, out var destination))
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                HelloAdminSecurity.ConfirmedDestinationRequired(
                    deliveryOptions.VerificationChannel));
        }

        var available = messageSender.CheckAvailability(
            deliveryOptions.VerificationChannel);
        if (!available.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                available.Errors);
        }

        var issued = await stepUp.BeginAsync(
            new BeginStepUpCommand(
                actor.Value.Id,
                HelloAdminSecurity.GetAction(command.Action),
                HelloAdminSecurity.CreateBinding(
                    actor.Value.Id,
                    command.TargetUserId,
                    command.Action,
                    command.Parameters,
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
                HelloAccountMessageKind.AdminActionVerification,
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

    public async Task<OperationResult<HelloAdminUserActionResult>>
        CompleteUserActionAsync(
            HelloAdminCompleteUserActionCommand command,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Parameters);

        var error = HelloAdminSecurity.Validate(
            command.TargetUserId,
            command.Action,
            command.Parameters);
        if (error is not null)
        {
            return OperationResultFactory.Fail<HelloAdminUserActionResult>(
                error);
        }

        var actor = await ValidateActorAsync(
            command.AccessToken,
            cancellationToken);
        if (!actor.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloAdminUserActionResult>(
                actor.Errors);
        }

        if (IsForbiddenSelfMutation(
                actor.Value.Id,
                command.TargetUserId,
                command.Action))
        {
            return OperationResultFactory.Fail<HelloAdminUserActionResult>(
                HelloAdminSecurity.SelfMutationForbidden());
        }

        if (!TryGetConfirmedDestination(actor.Value, out var destination))
        {
            return OperationResultFactory.Fail<HelloAdminUserActionResult>(
                HelloAdminSecurity.ConfirmedDestinationRequired(
                    deliveryOptions.VerificationChannel));
        }

        var verified = await verification.VerifyAsync(
            new VerifyVerificationChallengeCommand(
                command.ChallengeId,
                actor.Value.Id,
                command.VerificationCode),
            cancellationToken);
        if (!verified.IsSuccess)
        {
            return IsRetryableVerificationResponse(verified.Errors)
                ? OperationResultFactory.Fail<HelloAdminUserActionResult>(
                    verified.Errors)
                : RestartRequired(verified.Errors);
        }

        var authorized = await stepUp.AuthorizeAsync(
            new AuthorizeStepUpCommand(
                actor.Value.Id,
                HelloAdminSecurity.GetAction(command.Action),
                HelloAdminSecurity.CreateBinding(
                    actor.Value.Id,
                    command.TargetUserId,
                    command.Action,
                    command.Parameters,
                    deliveryOptions.VerificationChannel,
                    destination!),
                command.ChallengeId,
                verified.Value.Token),
            cancellationToken);
        if (!authorized.IsSuccess)
        {
            return RestartRequired(authorized.Errors);
        }

        var mutated = await MutateAsync(
            actor.Value.Id,
            command.TargetUserId,
            command.Action,
            command.Parameters,
            cancellationToken);
        return mutated.IsSuccess
            || mutated.Errors.Any(error => string.Equals(
                error.Code,
                HelloAdminErrorCodes.SessionCleanupRequired,
                StringComparison.Ordinal))
            ? mutated
            : RestartRequired(mutated.Errors);
    }

    private async Task<OperationResult<HelloAdminUserActionResult>>
        MutateAsync(
            Guid actorUserId,
            Guid targetUserId,
            HelloAdminUserAction action,
            HelloAdminUserActionParameters parameters,
            CancellationToken cancellationToken)
    {
        switch (action)
        {
            case HelloAdminUserAction.Block:
                {
                    var blocked = await users.BlockAsync(
                        new BlockUserCommand(
                            targetUserId,
                            parameters.ExpectedVersion!.Value,
                            parameters.BlockedUntil,
                            parameters.Reason),
                        cancellationToken);
                    return await FinishUserMutationAsync(
                        actorUserId,
                        blocked,
                        revokeSessions: true,
                        cancellationToken);
                }

            case HelloAdminUserAction.Unblock:
                {
                    var unblocked = await users.UnblockAsync(
                        new UnblockUserCommand(
                            targetUserId,
                            parameters.ExpectedVersion!.Value),
                        cancellationToken);
                    return await FinishUserMutationAsync(
                        actorUserId,
                        unblocked,
                        revokeSessions: false,
                        cancellationToken);
                }

            case HelloAdminUserAction.Delete:
                {
                    var deleted = await users.DeleteAsync(
                        new DeleteUserCommand(
                            targetUserId,
                            parameters.ExpectedVersion!.Value,
                            parameters.Reason),
                        cancellationToken);
                    if (!deleted.IsSuccess)
                    {
                        return OperationResultFactory.Fail<
                            HelloAdminUserActionResult>(deleted.Errors);
                    }

                    var revoked = await RevokeSessionsAsync(
                        targetUserId,
                        cancellationToken);
                    return revoked.IsSuccess
                        ? OperationResultFactory.Success(
                            new HelloAdminUserActionResult(
                                User: null,
                                SessionsRevoked: true))
                        : SessionCleanupRequired(revoked.Errors);
                }

            case HelloAdminUserAction.Restore:
                {
                    var restored = await users.RestoreAsync(
                        new RestoreUserCommand(
                            targetUserId,
                            parameters.ExpectedVersion!.Value),
                        cancellationToken);
                    return await FinishUserMutationAsync(
                        actorUserId,
                        restored,
                        revokeSessions: false,
                        cancellationToken);
                }

            case HelloAdminUserAction.RevokeSessions:
                {
                    var revoked = await RevokeSessionsAsync(
                        targetUserId,
                        cancellationToken);
                    return revoked.IsSuccess
                        ? OperationResultFactory.Success(
                            new HelloAdminUserActionResult(
                                User: null,
                                SessionsRevoked: true))
                        : OperationResultFactory.Fail<
                            HelloAdminUserActionResult>(revoked.Errors);
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    private async Task<OperationResult<HelloAdminUserActionResult>>
        FinishUserMutationAsync(
            Guid actorUserId,
            OperationResult<IdentityUser<TProfile>> mutated,
            bool revokeSessions,
            CancellationToken cancellationToken)
    {
        if (!mutated.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloAdminUserActionResult>(
                mutated.Errors);
        }

        if (revokeSessions)
        {
            var revoked = await RevokeSessionsAsync(
                mutated.Value.Id,
                cancellationToken);
            if (!revoked.IsSuccess)
            {
                return SessionCleanupRequired(revoked.Errors);
            }
        }

        var projected = await ProjectAsync(
            actorUserId,
            mutated.Value,
            cancellationToken);
        return OperationResultFactory.Success(
            new HelloAdminUserActionResult(
                projected.IsSuccess ? projected.Value : null,
                revokeSessions));
    }

    private Task<OperationResult<IdentityUser<TProfile>>> ValidateActorAsync(
        string accessToken,
        CancellationToken cancellationToken)
        => sessions.ValidateAccessTokenAsync(
            accessToken,
            cancellationToken);

    private Task<OperationResult> RevokeSessionsAsync(
        Guid userId,
        CancellationToken cancellationToken)
        => sessions.RevokeAllAsync(
            new RevokeAllIdentitySessionsCommand(userId),
            cancellationToken);

    private async Task<OperationResult<HelloAdminUser>> ProjectAsync(
        Guid actorUserId,
        IdentityUser<TProfile> user,
        CancellationToken cancellationToken)
    {
        var profile = await profiles.ProjectAsync(
            user.Profile,
            new HelloAdminProfileProjectionContext(
                actorUserId,
                user.Id,
                user.Flags),
            cancellationToken);
        return profile.IsSuccess
            ? OperationResultFactory.Success(
                new HelloAdminUser(
                    user.Id,
                    user.Flags,
                    user.UserName,
                    user.Email,
                    user.EmailConfirmed,
                    user.Phone,
                    user.PhoneConfirmed,
                    profile.Value,
                    user.Version,
                    user.DeletedAt,
                    user.BlockedAt,
                    user.BlockedUntil,
                    user.CreatedAt,
                    user.ModifiedAt))
            : OperationResultFactory.Fail<HelloAdminUser>(profile.Errors);
    }

    private bool TryGetConfirmedDestination(
        IdentityUser<TProfile> actor,
        out string? destination)
    {
        destination = deliveryOptions.VerificationChannel switch
        {
            HelloDeliveryChannel.Email
                when actor.EmailConfirmed
                    && !string.IsNullOrWhiteSpace(actor.Email) =>
                actor.Email,
            HelloDeliveryChannel.Sms
                when actor.PhoneConfirmed
                    && !string.IsNullOrWhiteSpace(actor.Phone) =>
                actor.Phone,
            HelloDeliveryChannel.Email or HelloDeliveryChannel.Sms => null,
            _ => throw new InvalidOperationException(
                "The configured verification channel is unsupported."),
        };
        return destination is not null;
    }

    private static bool IsForbiddenSelfMutation(
        Guid actorUserId,
        Guid targetUserId,
        HelloAdminUserAction action)
        => actorUserId == targetUserId
            && action is HelloAdminUserAction.Block
                or HelloAdminUserAction.Delete;

    private static bool IsRetryableVerificationResponse(
        IReadOnlyCollection<Error> errors)
        => errors.Count == 1
            && string.Equals(
                errors.First().Code,
                IdentityErrorCodes.VerificationResponseInvalid,
                StringComparison.Ordinal);

    private static OperationResult<HelloAdminUserActionResult>
        RestartRequired(IReadOnlyCollection<Error> causes)
        => OperationResultFactory.Fail<HelloAdminUserActionResult>(
            causes.Prepend(HelloAdminSecurity.RestartRequired()).ToArray());

    private static OperationResult<HelloAdminUserActionResult>
        SessionCleanupRequired(IReadOnlyCollection<Error> causes)
        => OperationResultFactory.Fail<HelloAdminUserActionResult>(
            causes.Prepend(
                    HelloAdminSecurity.SessionCleanupRequired())
                .ToArray());
}
