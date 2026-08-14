using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Roles;
using Skopka.Identity.Roles.Queries;
using Skopka.Identity.Users;
using Skopka.Identity.Users.Queries;

namespace Skopka.Hello.Admin;

public static class HelloAdminErrorCodes
{
    public const string SelfMutationForbidden =
        "hello.admin.self_mutation_forbidden";

    public const string ConfirmedDestinationRequired =
        "hello.admin.confirmed_destination_required";

    public const string RestartRequired =
        "hello.admin.action_restart_required";

    public const string SessionCleanupRequired =
        "hello.admin.session_cleanup_required";

    public const string ProtectedRoleMutationForbidden =
        "hello.admin.protected_role_mutation_forbidden";

    public const string SelfRoleRemovalForbidden =
        "hello.admin.self_role_removal_forbidden";
}

public static class HelloAdminSecurityEventTypes
{
    public const string RoleCreated = "hello.admin.role.created";

    public const string RoleUpdated = "hello.admin.role.updated";

    public const string RoleDeleted = "hello.admin.role.deleted";
}

public enum HelloAdminUserAction
{
    Block = 0,
    Unblock = 1,
    Delete = 2,
    Restore = 3,
    RevokeSessions = 4,
    ResetAuthenticator = 5,
}

public sealed record HelloAdminProfileProjectionContext(
    Guid ActorUserId,
    Guid TargetUserId,
    UserFlags TargetFlags);

public sealed record HelloAdminProfileField(
    string Name,
    string Label,
    string? Value);

public interface IHelloAdminProfileProjector<TProfile>
{
    Task<OperationResult<IReadOnlyList<HelloAdminProfileField>>>
        ProjectAsync(
            TProfile profile,
            HelloAdminProfileProjectionContext context,
            CancellationToken cancellationToken);
}

public sealed record HelloAdminUser(
    Guid Id,
    UserFlags Flags,
    string? UserName,
    string? Email,
    bool EmailConfirmed,
    string? Phone,
    bool PhoneConfirmed,
    IReadOnlyList<HelloAdminProfileField> Profile,
    long Version,
    DateTimeOffset? DeletedAt,
    DateTimeOffset? BlockedAt,
    DateTimeOffset? BlockedUntil,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt);

public sealed record HelloAdminUserPage(
    IReadOnlyList<HelloAdminUser> Items,
    IdentityUserCursor? NextCursor);

public sealed record HelloAdminQueryUsersCommand(
    string AccessToken,
    string? Search = null,
    IdentityUserStatus Status = IdentityUserStatus.Any,
    UserFlags RequiredFlags = UserFlags.None,
    int PageSize = IdentityUserQueryLimits.DefaultPageSize,
    IdentityUserCursor? Cursor = null);

public sealed record HelloAdminUserActionParameters(
    long? ExpectedVersion = null,
    DateTimeOffset? BlockedUntil = null,
    string? Reason = null);

public sealed record HelloAdminBeginUserActionCommand(
    string AccessToken,
    Guid TargetUserId,
    HelloAdminUserAction Action,
    HelloAdminUserActionParameters Parameters,
    string? ClientKey);

public sealed record HelloAdminCompleteUserActionCommand(
    string AccessToken,
    Guid TargetUserId,
    HelloAdminUserAction Action,
    HelloAdminUserActionParameters Parameters,
    Guid ChallengeId,
    string VerificationCode,
    string? ClientKey = null);

public sealed record HelloAdminUserActionResult(
    HelloAdminUser? User,
    bool SessionsRevoked);

public interface IHelloAdminApplication
{
    Task<OperationResult<HelloAdminUserPage>> QueryUsersAsync(
        HelloAdminQueryUsersCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloStepUpChallenge>> BeginUserActionAsync(
        HelloAdminBeginUserActionCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloAdminUserActionResult>>
        CompleteUserActionAsync(
            HelloAdminCompleteUserActionCommand command,
            CancellationToken cancellationToken);
}

public enum HelloAdminRoleAction
{
    Create = 0,
    Update = 1,
    Delete = 2,
    Assign = 3,
    Remove = 4,
}

public sealed record HelloAdminQueryRolesCommand(
    string AccessToken,
    string? Search = null,
    int PageSize = IdentityRoleQueryLimits.DefaultPageSize,
    IdentityRoleCursor? Cursor = null);

public sealed record HelloAdminGetUserRolesCommand(
    string AccessToken,
    Guid TargetUserId);

public sealed record HelloAdminRoleActionParameters(
    long? ExpectedVersion = null,
    string? Name = null,
    string? Description = null,
    Guid? ParentId = null);

public sealed record HelloAdminBeginRoleActionCommand(
    string AccessToken,
    HelloAdminRoleAction Action,
    Guid? RoleId,
    Guid? TargetUserId,
    HelloAdminRoleActionParameters Parameters,
    string? ClientKey);

public sealed record HelloAdminCompleteRoleActionCommand(
    string AccessToken,
    HelloAdminRoleAction Action,
    Guid? RoleId,
    Guid? TargetUserId,
    HelloAdminRoleActionParameters Parameters,
    Guid ChallengeId,
    string VerificationCode,
    string? ClientKey = null);

public sealed record HelloAdminRoleActionResult(
    IdentityRole? Role,
    Guid? TargetUserId,
    bool SessionsRevoked);

public interface IHelloAdminRoleApplication
{
    Task<OperationResult<IdentityRolePage>> QueryRolesAsync(
        HelloAdminQueryRolesCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<IReadOnlyList<IdentityRole>>> GetUserRolesAsync(
        HelloAdminGetUserRolesCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloStepUpChallenge>> BeginRoleActionAsync(
        HelloAdminBeginRoleActionCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<HelloAdminRoleActionResult>>
        CompleteRoleActionAsync(
            HelloAdminCompleteRoleActionCommand command,
            CancellationToken cancellationToken);
}
