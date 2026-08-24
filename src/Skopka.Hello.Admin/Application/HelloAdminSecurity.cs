using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;
using Skopka.Identity.Roles;
using Skopka.Identity.StepUp;
using Skopka.Identity.Verification;

namespace Skopka.Hello.Admin;

internal static class HelloAdminSecurity
{
    public const int MaximumReasonLength = 512;

    public const string BlockAction = "admin.user.block";
    public const string UnblockAction = "admin.user.unblock";
    public const string DeleteAction = "admin.user.delete";
    public const string RestoreAction = "admin.user.restore";
    public const string RevokeSessionsAction =
        "admin.user.sessions.revoke";
    public const string ResetAuthenticatorAction =
        "admin.user.authenticator.reset";

    public const string CreateRoleAction = "admin.role.create";
    public const string UpdateRoleAction = "admin.role.update";
    public const string DeleteRoleAction = "admin.role.delete";
    public const string AssignRoleAction = "admin.user.role.assign";
    public const string RemoveRoleAction = "admin.user.role.remove";

    public static string GetAction(HelloAdminUserAction action)
        => action switch
        {
            HelloAdminUserAction.Block => BlockAction,
            HelloAdminUserAction.Unblock => UnblockAction,
            HelloAdminUserAction.Delete => DeleteAction,
            HelloAdminUserAction.Restore => RestoreAction,
            HelloAdminUserAction.RevokeSessions =>
                RevokeSessionsAction,
            HelloAdminUserAction.ResetAuthenticator =>
                ResetAuthenticatorAction,
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

    public static string GetAction(HelloAdminRoleAction action)
        => action switch
        {
            HelloAdminRoleAction.Create => CreateRoleAction,
            HelloAdminRoleAction.Update => UpdateRoleAction,
            HelloAdminRoleAction.Delete => DeleteRoleAction,
            HelloAdminRoleAction.Assign => AssignRoleAction,
            HelloAdminRoleAction.Remove => RemoveRoleAction,
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

    public static bool TryParseActionSlug(
        string? value,
        out HelloAdminUserAction action)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            action = (HelloAdminUserAction)(-1);
            return false;
        }

        action = value.ToLowerInvariant() switch
        {
            "block" => HelloAdminUserAction.Block,
            "unblock" => HelloAdminUserAction.Unblock,
            "delete" => HelloAdminUserAction.Delete,
            "restore" => HelloAdminUserAction.Restore,
            "revoke-sessions" =>
                HelloAdminUserAction.RevokeSessions,
            "reset-authenticator" =>
                HelloAdminUserAction.ResetAuthenticator,
            _ => (HelloAdminUserAction)(-1),
        };
        return Enum.IsDefined(action);
    }

    public static string GetActionSlug(HelloAdminUserAction action)
        => action switch
        {
            HelloAdminUserAction.Block => "block",
            HelloAdminUserAction.Unblock => "unblock",
            HelloAdminUserAction.Delete => "delete",
            HelloAdminUserAction.Restore => "restore",
            HelloAdminUserAction.RevokeSessions => "revoke-sessions",
            HelloAdminUserAction.ResetAuthenticator =>
                "reset-authenticator",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

    public static bool TryParseRoleActionSlug(
        string? value,
        out HelloAdminRoleAction action)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            action = (HelloAdminRoleAction)(-1);
            return false;
        }

        action = value.ToLowerInvariant() switch
        {
            "create" => HelloAdminRoleAction.Create,
            "update" => HelloAdminRoleAction.Update,
            "delete" => HelloAdminRoleAction.Delete,
            "assign" => HelloAdminRoleAction.Assign,
            "remove" => HelloAdminRoleAction.Remove,
            _ => (HelloAdminRoleAction)(-1),
        };
        return Enum.IsDefined(action);
    }

    public static string GetActionSlug(HelloAdminRoleAction action)
        => action switch
        {
            HelloAdminRoleAction.Create => "create",
            HelloAdminRoleAction.Update => "update",
            HelloAdminRoleAction.Delete => "delete",
            HelloAdminRoleAction.Assign => "assign",
            HelloAdminRoleAction.Remove => "remove",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

    public static string CreateBinding(
        Guid actorUserId,
        Guid targetUserId,
        HelloAdminUserAction action,
        HelloAdminUserActionParameters parameters,
        HelloDeliveryChannel channel,
        string destination)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        var reason = parameters.Reason ?? string.Empty;
        var reasonHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(reason)));
        var destinationHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(destination)));
        var until = parameters.BlockedUntil?
            .ToUniversalTime()
            .ToString("O", CultureInfo.InvariantCulture)
            ?? "-";
        var version = parameters.ExpectedVersion?
            .ToString(CultureInfo.InvariantCulture)
            ?? "-";
        var value = string.Join(
            '|',
            "hello-admin-binding:v1",
            actorUserId.ToString("D"),
            targetUserId.ToString("D"),
            GetAction(action),
            version,
            until,
            reasonHash,
            ((int)channel).ToString(CultureInfo.InvariantCulture),
            destinationHash);
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    public static string CreateBinding(
        Guid actorUserId,
        Guid targetUserId,
        HelloAdminUserAction action,
        HelloAdminUserActionParameters parameters,
        HelloStepUpMethodSelection selection)
        => CreateBinding(
            actorUserId,
            targetUserId,
            action,
            parameters,
            selection.Channel,
            selection.Destination ?? "totp");

    public static Error? Validate(
        Guid targetUserId,
        HelloAdminUserAction action,
        HelloAdminUserActionParameters parameters)
    {
        if (targetUserId == Guid.Empty)
        {
            return Validation("targetUserId", "A target user is required.");
        }

        if (!Enum.IsDefined(action))
        {
            return Validation("action", "The admin action is invalid.");
        }

        if (parameters.Reason?.Length > MaximumReasonLength)
        {
            return Validation(
                "reason",
                $"Reason cannot exceed {MaximumReasonLength} characters.");
        }

        var requiresVersion = action is not HelloAdminUserAction.RevokeSessions
            and not HelloAdminUserAction.ResetAuthenticator;
        if (requiresVersion && parameters.ExpectedVersion is null)
        {
            return Validation(
                "expectedVersion",
                "ExpectedVersion is required for this action.");
        }

        if (!requiresVersion && parameters.ExpectedVersion is not null)
        {
            return Validation(
                "expectedVersion",
                "ExpectedVersion is not accepted for this action.");
        }

        if (action != HelloAdminUserAction.Block
            && parameters.BlockedUntil is not null)
        {
            return Validation(
                "blockedUntil",
                "BlockedUntil is accepted only for block actions.");
        }

        if (action == HelloAdminUserAction.Block
            && parameters.BlockedUntil <= DateTimeOffset.UtcNow)
        {
            return Validation(
                "blockedUntil",
                "BlockedUntil must be in the future.");
        }

        if (action is HelloAdminUserAction.Unblock
            or HelloAdminUserAction.Restore
            or HelloAdminUserAction.RevokeSessions
            or HelloAdminUserAction.ResetAuthenticator
            && !string.IsNullOrEmpty(parameters.Reason))
        {
            return Validation(
                "reason",
                "Reason is not accepted for this action.");
        }

        return null;
    }

    public static string CreateBinding(
        Guid actorUserId,
        HelloAdminRoleAction action,
        Guid? roleId,
        Guid? targetUserId,
        HelloAdminRoleActionParameters parameters,
        HelloDeliveryChannel channel,
        string destination)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        var nameHash = Hash(parameters.Name ?? string.Empty);
        var descriptionHash = Hash(parameters.Description ?? string.Empty);
        var destinationHash = Hash(destination);
        var version = parameters.ExpectedVersion?
            .ToString(CultureInfo.InvariantCulture)
            ?? "-";
        var value = string.Join(
            '|',
            "hello-admin-role-binding:v1",
            actorUserId.ToString("D"),
            GetAction(action),
            roleId?.ToString("D") ?? "-",
            targetUserId?.ToString("D") ?? "-",
            version,
            nameHash,
            descriptionHash,
            parameters.ParentId?.ToString("D") ?? "-",
            ((int)channel).ToString(CultureInfo.InvariantCulture),
            destinationHash);
        return Hash(value);
    }

    public static string CreateBinding(
        Guid actorUserId,
        HelloAdminRoleAction action,
        Guid? roleId,
        Guid? targetUserId,
        HelloAdminRoleActionParameters parameters,
        HelloStepUpMethodSelection selection)
        => CreateBinding(
            actorUserId,
            action,
            roleId,
            targetUserId,
            parameters,
            selection.Channel,
            selection.Destination ?? "totp");

    public static Error? Validate(
        HelloAdminRoleAction action,
        Guid? roleId,
        Guid? targetUserId,
        HelloAdminRoleActionParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (!Enum.IsDefined(action))
        {
            return Validation("action", "The role action is invalid.");
        }

        var isCreate = action == HelloAdminRoleAction.Create;
        var isUpdate = action == HelloAdminRoleAction.Update;
        var isDelete = action == HelloAdminRoleAction.Delete;
        var isMembership = action is HelloAdminRoleAction.Assign
            or HelloAdminRoleAction.Remove;

        if (isCreate
                ? roleId is not null
                : roleId is null || roleId == Guid.Empty)
        {
            return Validation(
                "roleId",
                isCreate
                    ? "RoleId is not accepted for role creation."
                    : "A role is required for this action.");
        }

        if (isMembership
            && (targetUserId is null || targetUserId == Guid.Empty))
        {
            return Validation(
                "targetUserId",
                "A target user is required for this action.");
        }

        if (!isMembership && targetUserId is not null)
        {
            return Validation(
                "targetUserId",
                "TargetUserId is accepted only for role membership actions.");
        }

        if (isUpdate || isDelete)
        {
            if (parameters.ExpectedVersion is null)
            {
                return Validation(
                    "expectedVersion",
                    "ExpectedVersion is required for this action.");
            }
        }
        else if (parameters.ExpectedVersion is not null)
        {
            return Validation(
                "expectedVersion",
                "ExpectedVersion is not accepted for this action.");
        }

        if (isCreate || isUpdate)
        {
            if (string.IsNullOrWhiteSpace(parameters.Name))
            {
                return Validation("name", "A role name is required.");
            }

            if (parameters.Name.Trim().Length
                > IdentityRoleLimits.MaximumNameLength)
            {
                return Validation(
                    "name",
                    $"Role name cannot exceed {IdentityRoleLimits.MaximumNameLength} characters.");
            }

            if (parameters.Description?.Trim().Length
                > IdentityRoleLimits.MaximumDescriptionLength)
            {
                return Validation(
                    "description",
                    $"Role description cannot exceed {IdentityRoleLimits.MaximumDescriptionLength} characters.");
            }
        }
        else if (parameters.Name is not null
            || parameters.Description is not null
            || parameters.ParentId is not null)
        {
            return Validation(
                "parameters",
                "Role fields are accepted only for create and update actions.");
        }

        return null;
    }

    public static Error SelfMutationForbidden()
        => new(
            HelloAdminErrorCodes.SelfMutationForbidden,
            "An administrator cannot block or delete their own account.",
            ErrorType.Forbidden);

    public static Error ConfirmedDestinationRequired(
        HelloDeliveryChannel channel)
        => new(
            HelloAdminErrorCodes.ConfirmedDestinationRequired,
            channel == HelloDeliveryChannel.Email
                ? "A confirmed administrator email address is required."
                : "A confirmed administrator phone number is required.",
            ErrorType.Forbidden);

    public static Error RestartRequired()
        => new(
            HelloAdminErrorCodes.RestartRequired,
            "The verification code can no longer be used. Request a new code and try again.",
            ErrorType.Conflict);

    public static Error SessionCleanupRequired()
        => new(
            HelloAdminErrorCodes.SessionCleanupRequired,
            "The user state changed, but active session cleanup did not complete.",
            ErrorType.Conflict);

    public static Error ProtectedRoleMutationForbidden()
        => new(
            HelloAdminErrorCodes.ProtectedRoleMutationForbidden,
            "A role used by an admin authorization policy cannot be updated or deleted through this surface.",
            ErrorType.Forbidden);

    public static Error RoleManagementDisabled()
        => new(
            HelloAdminErrorCodes.RoleManagementDisabled,
            "Role creation, update and deletion are disabled by the host.",
            ErrorType.Forbidden);

    public static Error SelfRoleRemovalForbidden()
        => new(
            HelloAdminErrorCodes.SelfRoleRemovalForbidden,
            "An administrator cannot remove their own role while that role protects an admin policy.",
            ErrorType.Forbidden);

    private static string Hash(string value)
        => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));

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
}

internal sealed class HelloAdminStepUpRequirementProvider<TProfile>
    : IHelloStepUpRequirementProvider<TProfile>
{
    private static readonly Dictionary<string, StepUpRequirement>
        Requirements = new(
            StringComparer.Ordinal)
        {
            [HelloAdminSecurity.BlockAction] = Create("block"),
            [HelloAdminSecurity.UnblockAction] = Create("unblock"),
            [HelloAdminSecurity.DeleteAction] = Create("delete"),
            [HelloAdminSecurity.RestoreAction] = Create("restore"),
            [HelloAdminSecurity.RevokeSessionsAction] =
                Create("sessions.revoke"),
            [HelloAdminSecurity.ResetAuthenticatorAction] =
                Create("authenticator.reset"),
            [HelloAdminSecurity.CreateRoleAction] =
                CreatePurpose("hello:admin.role.create"),
            [HelloAdminSecurity.UpdateRoleAction] =
                CreatePurpose("hello:admin.role.update"),
            [HelloAdminSecurity.DeleteRoleAction] =
                CreatePurpose("hello:admin.role.delete"),
            [HelloAdminSecurity.AssignRoleAction] =
                CreatePurpose("hello:admin.user.role.assign"),
            [HelloAdminSecurity.RemoveRoleAction] =
                CreatePurpose("hello:admin.user.role.remove"),
        };

    public Task<StepUpRequirement?> GetRequirementAsync(
        StepUpAuthorizationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        Requirements.TryGetValue(context.Action, out var requirement);
        return Task.FromResult(requirement);
    }

    private static StepUpRequirement Create(string purposeSuffix)
        => CreatePurpose($"hello:admin.user.{purposeSuffix}");

    private static StepUpRequirement CreatePurpose(string purpose)
        => new(
            purpose,
            [VerificationMethods.OneTimeCode],
            AssuranceLevel: 2,
            MaximumAge: TimeSpan.FromMinutes(5));
}
