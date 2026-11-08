using Microsoft.Extensions.Logging;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity;
using Skopka.Identity.Errors;
using Skopka.Identity.Sessions;
using Skopka.Identity.StepUp;
using Skopka.Identity.StepUp.Commands;
using Skopka.Identity.Totp;
using Skopka.Identity.Tokens;
using Skopka.Identity.Users;
using Skopka.Identity.Users.Commands;
using Skopka.Identity.Users.Queries;
using Skopka.Identity.Verification;

namespace Skopka.Hello.Admin;

internal sealed partial class HelloAdminApplication<TProfile>(
    IIdentityUserQueryService<TProfile> userQueries,
    IIdentityUserService<TProfile> users,
    IIdentitySessionService<TProfile> sessions,
    IIdentityStepUpService<TProfile> stepUp,
    IIdentityVerificationService<TProfile> verification,
    IIdentityActionTokenIssuer<TProfile> actionTokens,
    IHelloAdminProfileProjector<TProfile> profiles,
    IHelloAccountMessageSender messageSender,
    HelloDeliveryOptions deliveryOptions,
    SkopkaHelloAdminOptions options,
    HelloStepUpMethodResolver<TProfile>? stepUpMethodResolver = null,
    IIdentityTotpService<TProfile>? totp = null,
    IHelloSecurityEventSink? securityEvents = null,
    ILogger<HelloAdminApplication<TProfile>>? logger = null)
    : IHelloAdminApplication
{
    private readonly HelloStepUpMethodResolver<TProfile> stepUpMethods =
        stepUpMethodResolver
        ?? new HelloStepUpMethodResolver<TProfile>(
            deliveryOptions,
            messageSender);

    public async Task<OperationResult<long>> CountUsersAsync(
        HelloAdminQueryUsersCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var actor = await ValidateActorAsync(
            command.AccessToken,
            cancellationToken);
        return actor.IsSuccess
            ? await userQueries.CountAsync(ToQuery(command), cancellationToken)
            : OperationResultFactory.Fail<long>(actor.Errors);
    }

    private static IdentityUserQuery ToQuery(HelloAdminQueryUsersCommand command)
        => new(
            command.Search,
            command.Status,
            command.RequiredFlags,
            command.PageSize,
            command.Cursor)
        {
            RoleIds = command.RoleIds,
        };

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
            ToQuery(command),
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

        if (command.Action == HelloAdminUserAction.ConfirmEmail
            && !options.ManualEmailConfirmationEnabled)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                HelloAdminSecurity.ManualEmailConfirmationDisabled());
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

        Guid? enrollmentId = null;
        if (command.Action == HelloAdminUserAction.ResetAuthenticator)
        {
            var snapshot = await GetResetEnrollmentAsync(command.TargetUserId, command.Parameters.ExpectedVersion!.Value, cancellationToken);
            if (!snapshot.IsSuccess)
            {
                return OperationResultFactory.Fail<HelloStepUpChallenge>(snapshot.Errors);
            }
            enrollmentId = snapshot.Value;
        }

        var selected = await stepUpMethods.SelectAsync(
            actor.Value,
            cancellationToken);
        if (!selected.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                selected.Errors);
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
                    selected.Value,
                    enrollmentId),
                selected.Value.Method,
                command.ClientKey),
            cancellationToken);
        if (!issued.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                issued.Errors);
        }

        var delivered = await DeliverStepUpAsync(
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

        if (command.Action == HelloAdminUserAction.ConfirmEmail
            && !options.ManualEmailConfirmationEnabled)
        {
            return OperationResultFactory.Fail<HelloAdminUserActionResult>(
                HelloAdminSecurity.ManualEmailConfirmationDisabled());
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

        Guid? enrollmentId = null;
        if (command.Action == HelloAdminUserAction.ResetAuthenticator)
        {
            var snapshot = await GetResetEnrollmentAsync(command.TargetUserId, command.Parameters.ExpectedVersion!.Value, cancellationToken);
            if (!snapshot.IsSuccess)
            {
                return RestartRequired(snapshot.Errors);
            }
            enrollmentId = snapshot.Value;
        }

        var verified = await verification.VerifyAsync(
            new VerifyVerificationChallengeCommand(
                command.ChallengeId,
                actor.Value.Id,
                command.VerificationCode,
                command.ClientKey),
            cancellationToken);
        if (!verified.IsSuccess)
        {
            return IsRetryableVerificationResponse(verified.Errors)
                ? OperationResultFactory.Fail<HelloAdminUserActionResult>(
                    verified.Errors)
                : RestartRequired(verified.Errors);
        }

        var selected = stepUpMethods.Resolve(
            actor.Value,
            verified.Value.Method,
            requireDelivery: false);
        if (!selected.IsSuccess)
        {
            return RestartRequired(selected.Errors);
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
                    selected.Value,
                    enrollmentId),
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
            enrollmentId,
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
            Guid? enrollmentId,
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

            case HelloAdminUserAction.ResetAuthenticator:
                {
                    if (totp is null)
                    {
                        return OperationResultFactory.Fail<
                            HelloAdminUserActionResult>(
                            new Error(
                                IdentityErrorCodes
                                    .VerificationMethodUnavailable,
                                "Authenticator support is not configured.",
                                ErrorType.Failure));
                    }

                    var reset = await totp.ResetAsync(
                        new ResetTotpCommand(targetUserId, parameters.ExpectedVersion!.Value, enrollmentId!.Value),
                        cancellationToken);
                    if (!reset.IsSuccess)
                    {
                        return OperationResultFactory.Fail<
                            HelloAdminUserActionResult>(reset.Errors);
                    }

                    ObserveAuthenticatorReset(actorUserId, targetUserId);
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

            case HelloAdminUserAction.ConfirmEmail:
                return await ConfirmEmailAsync(
                    actorUserId,
                    targetUserId,
                    parameters.ExpectedVersion!.Value,
                    cancellationToken);

            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    private async Task<OperationResult<Guid>> GetResetEnrollmentAsync(
        Guid userId, long expectedVersion, CancellationToken cancellationToken)
    {
        var queried = await userQueries.QueryAsync(
            new IdentityUserQuery(Search: userId.ToString("D"), PageSize: 1), cancellationToken);
        if (!queried.IsSuccess) return OperationResultFactory.Fail<Guid>(queried.Errors);
        var target = queried.Value.Items.SingleOrDefault(user => user.Id == userId && user.DeletedAt is null);
        if (target is null) return OperationResultFactory.Fail<Guid>(HelloAdminSecurity.UserNotFound());
        if (target.Version != expectedVersion) return OperationResultFactory.Fail<Guid>(HelloAdminSecurity.ConcurrencyConflict());
        if (totp is null)
        {
            return OperationResultFactory.Fail<Guid>(new Error(
                IdentityErrorCodes.VerificationMethodUnavailable,
                "Authenticator support is not configured.", ErrorType.Failure));
        }

        var status = await totp.GetStatusAsync(userId, cancellationToken);
        if (!status.IsSuccess)
        {
            return OperationResultFactory.Fail<Guid>(status.Errors);
        }
        if (!status.Value.IsEnabled || status.Value.EnrollmentId is null)
        {
            return OperationResultFactory.Fail<Guid>(new Error(
                IdentityErrorCodes.TotpNotEnabled,
                "An authenticator is not enabled.", ErrorType.Conflict));
        }
        return OperationResultFactory.Success(status.Value.EnrollmentId.Value);
    }

    private void ObserveAuthenticatorReset(Guid actorUserId, Guid targetUserId)
    {
        var eventId = Guid.NewGuid();
        try
        {
            var result = securityEvents?.Write(new HelloSecurityEventEnvelope(
                eventId, HelloAdminSecurityEventTypes.AuthenticatorReset,
                targetUserId, actorUserId, null, null, DateTimeOffset.UtcNow,
                new Dictionary<string, string>()));
            if (result is not null && !result.IsSuccess && logger is not null)
            {
                SecurityEventSinkFailed(logger, eventId, null);
            }
        }
        catch (Exception exception)
        {
            if (logger is not null)
            {
                SecurityEventSinkFailed(logger, eventId, exception);
            }
        }
    }

    [LoggerMessage(
        EventId = 2102,
        Level = LogLevel.Error,
        Message = "Admin authenticator reset security-event sink failed for event {eventId}.")]
    private static partial void SecurityEventSinkFailed(
        ILogger logger, Guid eventId, Exception? exception);

    private async Task<OperationResult<HelloAdminUserActionResult>>
        ConfirmEmailAsync(
            Guid actorUserId,
            Guid targetUserId,
            long expectedVersion,
            CancellationToken cancellationToken)
    {
        var queried = await userQueries.QueryAsync(
            new IdentityUserQuery(
                Search: targetUserId.ToString("D"),
                PageSize: 1),
            cancellationToken);
        if (!queried.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloAdminUserActionResult>(
                queried.Errors);
        }

        var target = queried.Value.Items.SingleOrDefault(user =>
            user.Id == targetUserId && user.DeletedAt is null);
        if (target is null)
        {
            return OperationResultFactory.Fail<HelloAdminUserActionResult>(
                HelloAdminSecurity.UserNotFound());
        }

        if (target.Version != expectedVersion)
        {
            return OperationResultFactory.Fail<HelloAdminUserActionResult>(
                HelloAdminSecurity.ConcurrencyConflict());
        }

        if (string.IsNullOrWhiteSpace(target.Email))
        {
            return OperationResultFactory.Fail<HelloAdminUserActionResult>(
                HelloAdminSecurity.EmailMissing());
        }

        if (target.EmailConfirmed)
        {
            return await FinishUserMutationAsync(
                actorUserId,
                OperationResultFactory.Success(target),
                revokeSessions: false,
                cancellationToken);
        }

        var issued = await actionTokens.IssueEmailConfirmationAsync(
            target.Id,
            cancellationToken);
        if (!issued.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloAdminUserActionResult>(
                issued.Errors);
        }

        var confirmed = await users.ConfirmEmailAsync(
            new ConfirmEmailCommand(
                target.Id,
                target.Email,
                issued.Value.Token),
            cancellationToken);
        return await FinishUserMutationAsync(
            actorUserId,
            confirmed,
            revokeSessions: false,
            cancellationToken);
    }

    private async Task<OperationResult> DeliverStepUpAsync(
        HelloStepUpMethodSelection selection,
        IssuedVerificationChallenge issued,
        CancellationToken cancellationToken)
    {
        if (selection.Channel == HelloDeliveryChannel.Authenticator)
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
                HelloAccountMessageKind.AdminActionVerification,
                selection.Channel,
                selection.Destination,
                null,
                issued.ExpiresAt,
                issued.DeliveryCode),
            cancellationToken);
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
                    user.ModifiedAt)
                {
                    PhoneScope = user.PhoneScope,
                })
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
                or HelloAdminUserAction.Delete
                or HelloAdminUserAction.ResetAuthenticator;

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
