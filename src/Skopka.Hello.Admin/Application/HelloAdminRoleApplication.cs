using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;
using Skopka.Identity.Roles;
using Skopka.Identity.Roles.Commands;
using Skopka.Identity.Roles.Queries;
using Skopka.Identity.Sessions;
using Skopka.Identity.StepUp;
using Skopka.Identity.StepUp.Commands;
using Skopka.Identity.Users;
using Skopka.Identity.Verification;

namespace Skopka.Hello.Admin;

internal sealed partial class HelloAdminRoleApplication<TProfile>(
    IIdentityRoleQueryService<TProfile> roleQueries,
    IIdentityRoleService<TProfile> roles,
    IIdentitySessionService<TProfile> sessions,
    IIdentityStepUpService<TProfile> stepUp,
    IIdentityVerificationService<TProfile> verification,
    IHelloAccountMessageSender messageSender,
    HelloDeliveryOptions deliveryOptions,
    SkopkaHelloAdminOptions options,
    IHelloSecurityEventSink securityEvents,
    IHttpContextAccessor httpContextAccessor,
    ILogger<HelloAdminRoleApplication<TProfile>>? logger = null,
    HelloStepUpMethodResolver<TProfile>? stepUpMethodResolver = null)
    : IHelloAdminRoleApplication
{
    private readonly HelloStepUpMethodResolver<TProfile> stepUpMethods =
        stepUpMethodResolver
        ?? new HelloStepUpMethodResolver<TProfile>(
            deliveryOptions,
            messageSender);

    public async Task<OperationResult<IdentityRolePage>> QueryRolesAsync(
        HelloAdminQueryRolesCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var actor = await ValidateActorAsync(
            command.AccessToken,
            cancellationToken);
        if (!actor.IsSuccess)
        {
            return OperationResultFactory.Fail<IdentityRolePage>(
                actor.Errors);
        }

        return await roleQueries.QueryAsync(
            new IdentityRoleQuery(
                command.Search,
                command.PageSize,
                command.Cursor),
            cancellationToken);
    }

    public async Task<OperationResult<IReadOnlyList<IdentityRole>>>
        GetUserRolesAsync(
            HelloAdminGetUserRolesCommand command,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.TargetUserId == Guid.Empty)
        {
            return OperationResultFactory.Fail<IReadOnlyList<IdentityRole>>(
                Validation(
                    "targetUserId",
                    "A target user is required."));
        }

        var actor = await ValidateActorAsync(
            command.AccessToken,
            cancellationToken);
        if (!actor.IsSuccess)
        {
            return OperationResultFactory.Fail<IReadOnlyList<IdentityRole>>(
                actor.Errors);
        }

        return await roles.GetUserRolesAsync(
            command.TargetUserId,
            cancellationToken);
    }

    public async Task<OperationResult<HelloStepUpChallenge>>
        BeginRoleActionAsync(
            HelloAdminBeginRoleActionCommand command,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Parameters);

        var error = HelloAdminSecurity.Validate(
            command.Action,
            command.RoleId,
            command.TargetUserId,
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

        var allowed = await ValidateRoleTargetAsync(
            actor.Value.Id,
            command.Action,
            command.RoleId,
            command.TargetUserId,
            cancellationToken);
        if (!allowed.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloStepUpChallenge>(
                allowed.Errors);
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
                    command.Action,
                    command.RoleId,
                    command.TargetUserId,
                    command.Parameters,
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

    public async Task<OperationResult<HelloAdminRoleActionResult>>
        CompleteRoleActionAsync(
            HelloAdminCompleteRoleActionCommand command,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Parameters);

        var error = HelloAdminSecurity.Validate(
            command.Action,
            command.RoleId,
            command.TargetUserId,
            command.Parameters);
        if (error is not null)
        {
            return OperationResultFactory.Fail<HelloAdminRoleActionResult>(
                error);
        }

        var actor = await ValidateActorAsync(
            command.AccessToken,
            cancellationToken);
        if (!actor.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloAdminRoleActionResult>(
                actor.Errors);
        }

        var allowed = await ValidateRoleTargetAsync(
            actor.Value.Id,
            command.Action,
            command.RoleId,
            command.TargetUserId,
            cancellationToken);
        if (!allowed.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloAdminRoleActionResult>(
                allowed.Errors);
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
                ? OperationResultFactory.Fail<HelloAdminRoleActionResult>(
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
                    command.Action,
                    command.RoleId,
                    command.TargetUserId,
                    command.Parameters,
                    selected.Value),
                command.ChallengeId,
                verified.Value.Token),
            cancellationToken);
        if (!authorized.IsSuccess)
        {
            return RestartRequired(authorized.Errors);
        }

        var mutated = await MutateAsync(
            actor.Value.Id,
            command,
            cancellationToken);
        return mutated.IsSuccess
            || mutated.Errors.Any(item => string.Equals(
                item.Code,
                HelloAdminErrorCodes.SessionCleanupRequired,
                StringComparison.Ordinal))
            ? mutated
            : RestartRequired(mutated.Errors);
    }

    private async Task<OperationResult<HelloAdminRoleActionResult>>
        MutateAsync(
            Guid actorUserId,
            HelloAdminCompleteRoleActionCommand command,
            CancellationToken cancellationToken)
    {
        switch (command.Action)
        {
            case HelloAdminRoleAction.Create:
                return FinishRoleMutation(
                    await roles.CreateAsync(
                        new CreateRoleCommand(
                            command.Parameters.Name!,
                            command.Parameters.Description,
                            command.Parameters.ParentId),
                        cancellationToken),
                    HelloAdminSecurityEventTypes.RoleCreated,
                    actorUserId);

            case HelloAdminRoleAction.Update:
                return FinishRoleMutation(
                    await roles.UpdateAsync(
                        new UpdateRoleCommand(
                            command.RoleId!.Value,
                            command.Parameters.ExpectedVersion!.Value,
                            command.Parameters.Name!,
                            command.Parameters.Description,
                            command.Parameters.ParentId),
                        cancellationToken),
                    HelloAdminSecurityEventTypes.RoleUpdated,
                    actorUserId);

            case HelloAdminRoleAction.Delete:
                {
                    var deleted = await roles.DeleteAsync(
                        new DeleteRoleCommand(
                            command.RoleId!.Value,
                            command.Parameters.ExpectedVersion!.Value),
                        cancellationToken);
                    return deleted.IsSuccess
                        ? DeletedRoleResult(
                            actorUserId,
                            command.RoleId.Value)
                        : OperationResultFactory.Fail<
                            HelloAdminRoleActionResult>(deleted.Errors);
                }

            case HelloAdminRoleAction.Assign:
            case HelloAdminRoleAction.Remove:
                {
                    var targetUserId = command.TargetUserId!.Value;
                    var changed = command.Action
                        == HelloAdminRoleAction.Assign
                        ? await roles.AssignAsync(
                            new AssignRoleCommand(
                                targetUserId,
                                command.RoleId!.Value),
                            cancellationToken)
                        : await roles.RemoveAsync(
                            new RemoveRoleCommand(
                                targetUserId,
                                command.RoleId!.Value),
                            cancellationToken);
                    if (!changed.IsSuccess)
                    {
                        return OperationResultFactory.Fail<
                            HelloAdminRoleActionResult>(changed.Errors);
                    }

                    var revoked = await sessions.RevokeAllAsync(
                        new RevokeAllIdentitySessionsCommand(targetUserId),
                        cancellationToken);
                    return revoked.IsSuccess
                        ? OperationResultFactory.Success(
                            new HelloAdminRoleActionResult(
                                Role: null,
                                targetUserId,
                                SessionsRevoked: true))
                        : SessionCleanupRequired(revoked.Errors);
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }
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

    private async Task<OperationResult> ValidateRoleTargetAsync(
        Guid actorUserId,
        HelloAdminRoleAction action,
        Guid? roleId,
        Guid? targetUserId,
        CancellationToken cancellationToken)
    {
        if (!options.RoleManagementEnabled
            && action is HelloAdminRoleAction.Create
                or HelloAdminRoleAction.Update
                or HelloAdminRoleAction.Delete)
        {
            return OperationResultFactory.Fail(
                HelloAdminSecurity.RoleManagementDisabled());
        }

        if (action == HelloAdminRoleAction.Create)
        {
            return OperationResultFactory.Success();
        }

        var role = await roles.FindByIdAsync(
            roleId!.Value,
            cancellationToken);
        if (role is null)
        {
            return OperationResultFactory.Fail(
                new Error(
                    IdentityErrorCodes.RoleNotFound,
                    "Role not found.",
                    ErrorType.NotFound));
        }

        if ((action is HelloAdminRoleAction.Update
                or HelloAdminRoleAction.Delete)
            && IsProtectedRole(role.Name))
        {
            return OperationResultFactory.Fail(
                HelloAdminSecurity.ProtectedRoleMutationForbidden());
        }

        if (action == HelloAdminRoleAction.Remove
            && actorUserId == targetUserId
            && IsProtectedRole(role.Name))
        {
            return OperationResultFactory.Fail(
                HelloAdminSecurity.SelfRoleRemovalForbidden());
        }

        if (action is HelloAdminRoleAction.Assign
            or HelloAdminRoleAction.Remove)
        {
            var target = await roles.GetUserRolesAsync(
                targetUserId!.Value,
                cancellationToken);
            if (!target.IsSuccess)
            {
                return OperationResultFactory.Fail(target.Errors);
            }
        }

        return OperationResultFactory.Success();
    }

    private bool IsProtectedRole(string roleName)
        => string.Equals(
                roleName,
                options.ReadRoleName.Trim(),
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                roleName,
                options.ManageRoleName.Trim(),
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                roleName,
                options.DeleteRoleName.Trim(),
                StringComparison.OrdinalIgnoreCase)
            || options.ProtectedRoleNames.Any(protectedRoleName =>
                !string.IsNullOrWhiteSpace(protectedRoleName)
                && string.Equals(
                    roleName,
                    protectedRoleName.Trim(),
                    StringComparison.OrdinalIgnoreCase));

    private Task<OperationResult<IdentityUser<TProfile>>> ValidateActorAsync(
        string accessToken,
        CancellationToken cancellationToken)
        => sessions.ValidateAccessTokenAsync(
            accessToken,
            cancellationToken);

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

    private OperationResult<HelloAdminRoleActionResult> FinishRoleMutation(
        OperationResult<IdentityRole> result,
        string eventType,
        Guid actorUserId)
    {
        if (!result.IsSuccess)
        {
            return OperationResultFactory.Fail<HelloAdminRoleActionResult>(
                result.Errors);
        }

        ObserveRoleMutation(eventType, actorUserId, result.Value.Id);
        return OperationResultFactory.Success(
            new HelloAdminRoleActionResult(
                result.Value,
                TargetUserId: null,
                SessionsRevoked: false));
    }

    private OperationResult<HelloAdminRoleActionResult> DeletedRoleResult(
        Guid actorUserId,
        Guid roleId)
    {
        ObserveRoleMutation(
            HelloAdminSecurityEventTypes.RoleDeleted,
            actorUserId,
            roleId);
        return OperationResultFactory.Success(
            new HelloAdminRoleActionResult(
                Role: null,
                TargetUserId: null,
                SessionsRevoked: false));
    }

    private void ObserveRoleMutation(
        string eventType,
        Guid actorUserId,
        Guid roleId)
    {
        var eventId = Guid.NewGuid();
        try
        {
            var result = securityEvents.Write(
                new HelloSecurityEventEnvelope(
                    eventId,
                    eventType,
                    SubjectUserId: null,
                    actorUserId,
                    roleId,
                    httpContextAccessor.HttpContext?.TraceIdentifier,
                    DateTimeOffset.UtcNow,
                    new Dictionary<string, string>()));
            if (!result.IsSuccess && logger is not null)
            {
                SecurityEventSinkFailed(
                    logger,
                    eventId,
                    eventType,
                    result.Errors.FirstOrDefault()?.Code
                        ?? HelloAuditErrorCodes.Failed,
                    null);
            }
        }
        catch (Exception exception)
        {
            if (logger is not null)
            {
                SecurityEventSinkFailed(
                    logger,
                    eventId,
                    eventType,
                    HelloAuditErrorCodes.Failed,
                    exception);
            }
        }
    }

    private static bool IsRetryableVerificationResponse(
        IReadOnlyCollection<Error> errors)
        => errors.Count == 1
            && string.Equals(
                errors.First().Code,
                IdentityErrorCodes.VerificationResponseInvalid,
                StringComparison.Ordinal);

    private static OperationResult<HelloAdminRoleActionResult>
        RestartRequired(IReadOnlyCollection<Error> causes)
        => OperationResultFactory.Fail<HelloAdminRoleActionResult>(
            causes.Prepend(HelloAdminSecurity.RestartRequired()).ToArray());

    private static OperationResult<HelloAdminRoleActionResult>
        SessionCleanupRequired(IReadOnlyCollection<Error> causes)
        => OperationResultFactory.Fail<HelloAdminRoleActionResult>(
            causes.Prepend(
                    HelloAdminSecurity.SessionCleanupRequired())
                .ToArray());

    private static Error Validation(string field, string message)
        => new(
            IdentityErrorCodes.Validation,
            "Validation failed.",
            ErrorType.Validation,
            new ValidationDetails(
                new Dictionary<string, string[]>
                {
                    [field] = [message],
                }));

    [LoggerMessage(
        EventId = 2101,
        Level = LogLevel.Error,
        Message = "Admin security-event sink failed for event {eventId}; type: {eventType}; error code: {errorCode}.")]
    private static partial void SecurityEventSinkFailed(
        ILogger logger,
        Guid eventId,
        string eventType,
        string errorCode,
        Exception? exception);
}
