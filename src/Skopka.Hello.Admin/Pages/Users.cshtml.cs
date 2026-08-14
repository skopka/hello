using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.UI;
using Skopka.Identity.Errors;
using Skopka.Identity.Roles;
using Skopka.Identity.Users.Queries;

namespace Skopka.Hello.Admin.Pages;

[Authorize(Policy = HelloUiDefaults.AuthorizationPolicy)]
public sealed class UsersModel(
    IHelloAdminApplication application,
    IHelloAdminRoleApplication roleApplication,
    IHelloRequestContext requestContext,
    IAuthorizationService authorization,
    SkopkaHelloAdminOptions options,
    IHelloUiLocalizer text)
    : PageModel
{
    public IReadOnlyList<HelloAdminUser> Users { get; private set; } = [];

    public IdentityUserCursor? NextCursor { get; private set; }

    public IReadOnlyDictionary<Guid, IReadOnlyList<IdentityRole>> UserRoles
    { get; private set; } =
        new Dictionary<Guid, IReadOnlyList<IdentityRole>>();

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
        if (!await IsAuthorizedAsync(options.ReadPolicyName))
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

        if (!await IsAuthorizedAsync(options.ReadPolicyName)
            || !await IsAuthorizedAsync(options.DeletePolicyName))
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

        if (!await IsAuthorizedAsync(options.ReadPolicyName)
            || !await IsAuthorizedAsync(options.DeletePolicyName))
        {
            return Forbid();
        }

        var accessToken = await ReadAccessTokenAsync();
        if (accessToken is null)
        {
            return Challenge(HelloUiDefaults.AuthenticationScheme);
        }

        var parameters = new HelloAdminRoleActionParameters();
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

    private async Task<IActionResult> ReloadPageAsync(
        CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedAsync(options.ReadPolicyName))
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
