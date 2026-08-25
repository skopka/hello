using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.UI;
using Skopka.Identity.Errors;
using Skopka.Identity.Roles;
using Skopka.Identity.Roles.Queries;
using Skopka.Identity.Users.Queries;

namespace Skopka.Hello.Admin.Pages;

[Authorize(Policy = HelloUiDefaults.AuthorizationPolicy)]
public sealed class UsersModel(
    IHelloAdminApplication application,
    IHelloAdminRoleApplication roleApplication,
    IHelloRequestContext requestContext,
    IAuthorizationService authorization,
    IHelloUiUserAccessor userAccessor,
    IHelloSessionCookieManager sessionCookies,
    SkopkaHelloAdminOptions options,
    IHelloUiLocalizer text)
    : PageModel
{
    private readonly HelloAdminRoleRulesEvaluator roleRules = new(options);

    public const int RoleCatalogPageSize =
        IdentityRoleQueryLimits.MaximumPageSize;

    public IReadOnlyList<HelloAdminUser> Users { get; private set; } = [];

    public IdentityUserCursor? NextCursor { get; private set; }

    public IReadOnlyDictionary<Guid, IReadOnlyList<IdentityRole>> UserRoles
    { get; private set; } =
        new Dictionary<Guid, IReadOnlyList<IdentityRole>>();

    public IReadOnlyList<IdentityRole> RoleCatalog { get; private set; } = [];

    public bool RoleCatalogHasMore { get; private set; }

    public bool CanReadUsers { get; private set; }

    public bool CanReadRoles { get; private set; }

    public bool CanManageUsers { get; private set; }

    public bool CanDeleteUsers { get; private set; }

    public bool CanAssignRoles { get; private set; }

    private HashSet<Guid> ManageableRoleIds { get; set; } = [];

    private Guid? ActorUserId { get; set; }

    private bool AuthorizationLoaded { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public IdentityUserStatus Status { get; set; } =
        IdentityUserStatus.Any;

    [BindProperty(SupportsGet = true)]
    public DateTimeOffset? CursorCreatedAt { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? CursorId { get; set; }

    public PendingAdminAction? PendingAction { get; private set; }

    public PendingAdminRoleAction? PendingRoleAction { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders();
        await LoadAuthorizationAsync();
        if (!CanReadUsers && !CanAssignRoles)
        {
            return Forbid();
        }

        if ((CursorCreatedAt is null) != (CursorId is null))
        {
            ModelState.AddModelError(
                string.Empty,
                text["Admin.Errors.IncompleteCursor"]);
            CursorCreatedAt = null;
            CursorId = null;
        }

        var accessToken = await ReadAccessTokenAsync();
        if (accessToken is null)
        {
            return Challenge(HelloUiDefaults.AuthenticationScheme);
        }

        await LoadUsersAsync(accessToken, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostBeginActionAsync(
        Guid userId,
        string action,
        long? expectedVersion,
        DateTimeOffset? blockedUntil,
        string? reason,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders();
        if (!HelloAdminSecurity.TryParseActionSlug(
                action,
                out var parsedAction))
        {
            ModelState.AddModelError(
                string.Empty,
                text["Admin.Errors.InvalidUserAction"]);
            return await ReloadPageAsync(cancellationToken);
        }

        if (!await IsAuthorizedAsync(options.ReadPolicyName)
            || !await IsAuthorizedAsync(GetPolicy(parsedAction)))
        {
            return Forbid();
        }

        var accessToken = await ReadAccessTokenAsync();
        if (accessToken is null)
        {
            return Challenge(HelloUiDefaults.AuthenticationScheme);
        }

        var parameters = new HelloAdminUserActionParameters(
            expectedVersion,
            blockedUntil,
            reason);
        var result = await application.BeginUserActionAsync(
            new HelloAdminBeginUserActionCommand(
                accessToken,
                userId,
                parsedAction,
                parameters,
                requestContext.CreateClientKey(HttpContext)),
            cancellationToken);
        if (result.IsSuccess)
        {
            PendingAction = new PendingAdminAction(
                userId,
                parsedAction,
                parameters,
                result.Value.ChallengeId,
                result.Value.ExpiresAt,
                result.Value.DeliveryChannel);
        }
        else
        {
            AddErrors(result.Errors);
        }

        await LoadUsersAsync(accessToken, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCompleteActionAsync(
        Guid userId,
        string action,
        Guid challengeId,
        string verificationCode,
        long? expectedVersion,
        DateTimeOffset? blockedUntil,
        string? reason,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders();
        if (!HelloAdminSecurity.TryParseActionSlug(
                action,
                out var parsedAction))
        {
            ModelState.AddModelError(
                string.Empty,
                text["Admin.Errors.InvalidUserAction"]);
            return await ReloadPageAsync(cancellationToken);
        }

        if (!await IsAuthorizedAsync(options.ReadPolicyName)
            || !await IsAuthorizedAsync(GetPolicy(parsedAction)))
        {
            return Forbid();
        }

        var accessToken = await ReadAccessTokenAsync();
        if (accessToken is null)
        {
            return Challenge(HelloUiDefaults.AuthenticationScheme);
        }

        var parameters = new HelloAdminUserActionParameters(
            expectedVersion,
            blockedUntil,
            reason);
        var result = await application.CompleteUserActionAsync(
            new HelloAdminCompleteUserActionCommand(
                accessToken,
                userId,
                parsedAction,
                parameters,
                challengeId,
                verificationCode,
                requestContext.CreateClientKey(HttpContext)),
            cancellationToken);
        if (result.IsSuccess)
        {
            StatusMessage = text[
                "Admin.Common.ActionCompleted",
                text[GetActionTextKey(parsedAction)]];
            return RedirectToPage(
                "/SkopkaHelloAdmin/Users",
                new
                {
                    Search,
                    Status,
                });
        }

        if (IsCommittedSessionCleanupFailure(result.Errors))
        {
            StatusMessage = string.Join(
                " ",
                result.Errors.Select(error =>
                    LocalizeError(error, null, error.Message)));
            return RedirectToPage(
                "/SkopkaHelloAdmin/Users",
                new
                {
                    Search,
                    Status,
                });
        }

        AddErrors(result.Errors);
        PendingAction = new PendingAdminAction(
            userId,
            parsedAction,
            parameters,
            challengeId,
            ExpiresAt: null,
            DeliveryChannel: null);
        await LoadUsersAsync(accessToken, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostBeginRoleActionAsync(
        Guid userId,
        Guid roleId,
        string action,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders();
        if (!HelloAdminSecurity.TryParseRoleActionSlug(
                action,
                out var parsedAction)
            || parsedAction is not HelloAdminRoleAction.Assign
                and not HelloAdminRoleAction.Remove)
        {
            ModelState.AddModelError(
                string.Empty,
                text["Admin.Errors.InvalidRoleMembershipAction"]);
            return await ReloadPageAsync(cancellationToken);
        }

        if (!await IsAuthorizedAsync(
                options.RoleAssignmentPolicyName))
        {
            return Forbid();
        }

        var accessToken = await ReadAccessTokenAsync();
        if (accessToken is null)
        {
            return Challenge(HelloUiDefaults.AuthenticationScheme);
        }

        var parameters = new HelloAdminRoleActionParameters();
        var result = await roleApplication.BeginRoleActionAsync(
            new HelloAdminBeginRoleActionCommand(
                accessToken,
                parsedAction,
                roleId,
                userId,
                parameters,
                requestContext.CreateClientKey(HttpContext)),
            cancellationToken);
        if (result.IsSuccess)
        {
            PendingRoleAction = new PendingAdminRoleAction(
                parsedAction,
                roleId,
                userId,
                parameters,
                result.Value.ChallengeId,
                result.Value.ExpiresAt,
                result.Value.DeliveryChannel);
        }
        else
        {
            AddErrors(result.Errors);
        }

        await LoadUsersAsync(accessToken, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCompleteRoleActionAsync(
        Guid userId,
        Guid roleId,
        string action,
        Guid challengeId,
        string verificationCode,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders();
        if (!HelloAdminSecurity.TryParseRoleActionSlug(
                action,
                out var parsedAction)
            || parsedAction is not HelloAdminRoleAction.Assign
                and not HelloAdminRoleAction.Remove)
        {
            ModelState.AddModelError(
                string.Empty,
                text["Admin.Errors.InvalidRoleMembershipAction"]);
            return await ReloadPageAsync(cancellationToken);
        }

        if (!await IsAuthorizedAsync(
                options.RoleAssignmentPolicyName))
        {
            return Forbid();
        }

        var accessToken = await ReadAccessTokenAsync();
        if (accessToken is null)
        {
            return Challenge(HelloUiDefaults.AuthenticationScheme);
        }

        var parameters = new HelloAdminRoleActionParameters();
        var currentUser = await userAccessor.GetAsync(
            HttpContext,
            cancellationToken);
        var result = await roleApplication.CompleteRoleActionAsync(
            new HelloAdminCompleteRoleActionCommand(
                accessToken,
                parsedAction,
                roleId,
                userId,
                parameters,
                challengeId,
                verificationCode,
                requestContext.CreateClientKey(HttpContext)),
            cancellationToken);
        if (result.IsSuccess)
        {
            if (result.Value.CurrentActorSessionRevoked)
            {
                await ClearLocalSessionAsync();
                return RedirectToPage(
                    "/SkopkaHello/Login",
                    new { rolesChanged = true });
            }

            StatusMessage = text[
                "Admin.Common.ActionCompleted",
                text[GetRoleActionTextKey(parsedAction)]];
            return RedirectToPage(
                "/SkopkaHelloAdmin/Users",
                new
                {
                    Search,
                    Status,
                });
        }

        if (IsCommittedSessionCleanupFailure(result.Errors))
        {
            if (parsedAction == HelloAdminRoleAction.Remove
                && currentUser?.UserId == userId)
            {
                await ClearLocalSessionAsync();
                return RedirectToPage(
                    "/SkopkaHello/Login",
                    new { rolesChangedSessionCleanup = true });
            }

            StatusMessage = string.Join(
                " ",
                result.Errors.Select(error =>
                    LocalizeError(error, null, error.Message)));
            return RedirectToPage(
                "/SkopkaHelloAdmin/Users",
                new
                {
                    Search,
                    Status,
                });
        }

        AddErrors(result.Errors);
        PendingRoleAction = new PendingAdminRoleAction(
            parsedAction,
            roleId,
            userId,
            parameters,
            challengeId,
            ExpiresAt: null,
            DeliveryChannel: null);
        await LoadUsersAsync(accessToken, cancellationToken);
        return Page();
    }

    private async Task ClearLocalSessionAsync()
    {
        sessionCookies.DeleteSessionCookies(HttpContext);
        await HttpContext.SignOutAsync(
            HelloUiDefaults.AuthenticationScheme);
    }

    public static string GetActionSlug(HelloAdminUserAction action)
        => HelloAdminSecurity.GetActionSlug(action);

    public static string GetActionLabel(HelloAdminUserAction action)
        => action switch
        {
            HelloAdminUserAction.Block => "Block user",
            HelloAdminUserAction.Unblock => "Unblock user",
            HelloAdminUserAction.Delete => "Delete user",
            HelloAdminUserAction.Restore => "Restore user",
            HelloAdminUserAction.RevokeSessions => "Revoke sessions",
            HelloAdminUserAction.ResetAuthenticator =>
                "Reset authenticator",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

    public static string GetRoleActionLabel(HelloAdminRoleAction action)
        => action switch
        {
            HelloAdminRoleAction.Assign => "Assign role",
            HelloAdminRoleAction.Remove => "Remove role",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

    public static string GetActionTextKey(HelloAdminUserAction action)
        => action switch
        {
            HelloAdminUserAction.Block => "Admin.Users.BlockUser",
            HelloAdminUserAction.Unblock => "Admin.Users.UnblockUser",
            HelloAdminUserAction.Delete => "Admin.Users.DeleteUser",
            HelloAdminUserAction.Restore => "Admin.Users.RestoreUser",
            HelloAdminUserAction.RevokeSessions =>
                "Admin.Users.RevokeSessions",
            HelloAdminUserAction.ResetAuthenticator =>
                "Admin.Users.ResetAuthenticator",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

    public static string GetRoleActionTextKey(HelloAdminRoleAction action)
        => action switch
        {
            HelloAdminRoleAction.Assign => "Admin.Roles.Assign",
            HelloAdminRoleAction.Remove => "Admin.Roles.Remove",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

    public bool CanRemoveRole(IdentityRole role, Guid targetUserId)
        => ManageableRoleIds.Contains(role.Id)
            && (ActorUserId != targetUserId
                || roleRules.GetProtection(role.Name) is not (
                    HelloRoleProtection.System
                        or HelloRoleProtection.Retained));

    private async Task<IActionResult> ReloadPageAsync(
        CancellationToken cancellationToken)
    {
        await LoadAuthorizationAsync();
        if (!CanReadUsers && !CanAssignRoles)
        {
            return Forbid();
        }

        var accessToken = await ReadAccessTokenAsync();
        if (accessToken is null)
        {
            return Challenge(HelloUiDefaults.AuthenticationScheme);
        }

        await LoadUsersAsync(accessToken, cancellationToken);
        return Page();
    }

    private async Task LoadUsersAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        await LoadAuthorizationAsync();
        var result = await application.QueryUsersAsync(
            new HelloAdminQueryUsersCommand(
                accessToken,
                Search,
                Status,
                PageSize: 25,
                Cursor: CursorCreatedAt is not null && CursorId is not null
                    ? new IdentityUserCursor(
                        CursorCreatedAt.Value,
                        CursorId.Value)
                    : null),
            cancellationToken);
        if (result.IsSuccess)
        {
            Users = result.Value.Items;
            NextCursor = result.Value.NextCursor;
            var roleCatalog = await roleApplication.QueryRolesAsync(
                new HelloAdminQueryRolesCommand(
                    accessToken,
                    PageSize: RoleCatalogPageSize),
                cancellationToken);
            if (!roleCatalog.IsSuccess)
            {
                AddErrors(roleCatalog.Errors);
                return;
            }

            RoleCatalogHasMore = roleCatalog.Value.NextCursor is not null;
            var currentUser = await userAccessor.GetAsync(
                HttpContext,
                cancellationToken);
            if (currentUser is null)
            {
                ModelState.AddModelError(
                    string.Empty,
                    text["Errors.identity.session.refresh_token_invalid"]);
                return;
            }

            ActorUserId = currentUser.UserId;

            var actorRoles = await roleApplication.GetUserRolesAsync(
                new HelloAdminGetUserRolesCommand(
                    accessToken,
                    currentUser.UserId),
                cancellationToken);
            if (!actorRoles.IsSuccess)
            {
                AddErrors(actorRoles.Errors);
                return;
            }

            var rolesByUser = new Dictionary<
                Guid,
                IReadOnlyList<IdentityRole>>();
            foreach (var user in Users)
            {
                if (user.DeletedAt is not null)
                {
                    rolesByUser[user.Id] = [];
                    continue;
                }

                var assigned = await roleApplication.GetUserRolesAsync(
                    new HelloAdminGetUserRolesCommand(
                        accessToken,
                        user.Id),
                    cancellationToken);
                if (!assigned.IsSuccess)
                {
                    AddErrors(assigned.Errors);
                    return;
                }

                rolesByUser[user.Id] = assigned.Value;
            }

            UserRoles = rolesByUser;
            var knownRoles = roleCatalog.Value.Items
                .Concat(rolesByUser.Values.SelectMany(
                    assignedRoles => assignedRoles))
                .GroupBy(role => role.Id)
                .Select(group => group.First())
                .ToArray();
            ManageableRoleIds = knownRoles
                .Where(role => roleRules.CanManageMembership(
                    actorRoles.Value,
                    role))
                .Select(role => role.Id)
                .ToHashSet();
            RoleCatalog = roleCatalog.Value.Items
                .Where(role => ManageableRoleIds.Contains(role.Id))
                .OrderBy(role => role.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return;
        }

        AddErrors(result.Errors);
    }

    private async Task<string?> ReadAccessTokenAsync()
    {
        var authenticated = await HttpContext.AuthenticateAsync(
            HelloUiDefaults.AuthenticationScheme);
        return authenticated.Succeeded
            ? authenticated.Properties?.GetTokenValue(
                HelloUiDefaults.AccessTokenName)
            : null;
    }

    private async Task<bool> IsAuthorizedAsync(string policyName)
        => (await authorization.AuthorizeAsync(
            User,
            HttpContext,
            policyName)).Succeeded;

    private async Task LoadAuthorizationAsync()
    {
        if (AuthorizationLoaded)
        {
            return;
        }

        CanReadUsers = await IsAuthorizedAsync(options.ReadPolicyName);
        CanReadRoles = CanReadUsers;
        CanManageUsers = await IsAuthorizedAsync(
            options.ManagePolicyName);
        CanDeleteUsers = await IsAuthorizedAsync(
            options.DeletePolicyName);
        CanAssignRoles = await IsAuthorizedAsync(
            options.RoleAssignmentPolicyName);
        AuthorizationLoaded = true;
    }

    private string GetPolicy(HelloAdminUserAction action)
        => action is HelloAdminUserAction.Delete
            or HelloAdminUserAction.ResetAuthenticator
            ? options.DeletePolicyName
            : options.ManagePolicyName;

    private void AddErrors(IReadOnlyCollection<Error> errors)
    {
        foreach (var error in errors)
        {
            if (error.Details is ValidationDetails validation)
            {
                foreach (var field in validation.Fields)
                {
                    foreach (var message in field.Value)
                    {
                        ModelState.AddModelError(
                            field.Key,
                            LocalizeError(error, field.Key, message));
                    }
                }

                continue;
            }

            ModelState.AddModelError(
                string.Empty,
                LocalizeError(error, null, error.Message));
        }
    }

    private string LocalizeError(
        Error error,
        string? field,
        string fallback)
    {
        if (field is not null
            && text.TryGetString(
                $"Errors.{error.Code}.{field}",
                out var fieldMessage))
        {
            return fieldMessage;
        }

        return text.TryGetString(
            $"Errors.{error.Code}",
            out var message)
                ? message
                : fallback;
    }

    private static bool IsCommittedSessionCleanupFailure(
        IReadOnlyCollection<Error> errors)
        => errors.Any(error => string.Equals(
            error.Code,
            HelloAdminErrorCodes.SessionCleanupRequired,
            StringComparison.Ordinal));

    private void ApplySensitiveResponseHeaders()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}

public sealed record PendingAdminAction(
    Guid UserId,
    HelloAdminUserAction Action,
    HelloAdminUserActionParameters Parameters,
    Guid ChallengeId,
    DateTimeOffset? ExpiresAt,
    HelloDeliveryChannel? DeliveryChannel);
