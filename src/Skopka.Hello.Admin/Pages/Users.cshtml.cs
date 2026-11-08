using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.UI;
using Skopka.Identity.Roles;
using Skopka.Identity.Roles.Queries;
using Skopka.Identity.Users.Queries;

namespace Skopka.Hello.Admin.Pages;

[Authorize(Policy = HelloUiDefaults.AuthorizationPolicy)]
public sealed class UsersModel : PageModel
{
    private readonly IHelloAdminApplication application;
    private readonly IHelloAdminRoleApplication roleApplication;
    private readonly IHelloRequestContext requestContext;
    private readonly IAuthorizationService authorization;
    private readonly IHelloUiUserAccessor userAccessor;
    private readonly IHelloSessionCookieManager sessionCookies;
    private readonly HelloUiPrgStateStore prgState;
    private readonly SkopkaHelloAdminOptions options;
    private readonly IHelloUiLocalizer text;
    private readonly HelloAdminRoleRulesEvaluator roleRules;

    public UsersModel(
        IHelloAdminApplication application,
        IHelloAdminRoleApplication roleApplication,
        IHelloRequestContext requestContext,
        IAuthorizationService authorization,
        IHelloUiUserAccessor userAccessor,
        IHelloSessionCookieManager sessionCookies,
        HelloUiPrgStateStore prgState,
        SkopkaHelloAdminOptions options,
        IHelloUiLocalizer text)
    {
        this.application = application;
        this.roleApplication = roleApplication;
        this.requestContext = requestContext;
        this.authorization = authorization;
        this.userAccessor = userAccessor;
        this.sessionCookies = sessionCookies;
        this.prgState = prgState;
        this.options = options;
        this.text = text;
        roleRules = new(options);
    }

    private HelloAdminUserDirectory directory
        => HttpContext.RequestServices.GetRequiredService<HelloAdminUserDirectory>();

    private HelloAdminCompletionGuard completionGuard
        => HttpContext.RequestServices.GetRequiredService<HelloAdminCompletionGuard>();

    public const int PageSize = 25;

    public const int RoleCatalogPageSize =
        IdentityRoleQueryLimits.MaximumPageSize;

    public IReadOnlyList<HelloAdminUser> Users { get; private set; } = [];

    public IdentityUserCursor? NextCursor { get; private set; }

    /// <summary>The badge filter stopped at the scan bound before the page filled.</summary>
    public bool ScanLimitReached { get; private set; }

    public int ScannedUsers { get; private set; }

    /// <summary>Number of accounts matching a role-badge filter, counted by Identity.</summary>
    public long? FilteredTotal { get; private set; }

    public IReadOnlyDictionary<Guid, IReadOnlyList<IdentityRole>> UserRoles
    { get; private set; } =
        new Dictionary<Guid, IReadOnlyList<IdentityRole>>();

    public IReadOnlyDictionary<Guid, IReadOnlyList<HelloAdminUserBadge>> UserBadges
    { get; private set; } =
        new Dictionary<Guid, IReadOnlyList<HelloAdminUserBadge>>();

    public IReadOnlyList<HelloAdminUserBadge> FilterBadges
        => options.UserBadges.Where(badge => badge.Filterable).ToArray();

    public IReadOnlyList<IdentityRole> RoleCatalog { get; private set; } = [];

    public bool RoleCatalogHasMore { get; private set; }

    public bool CanReadUsers { get; private set; }

    public bool CanReadRoles { get; private set; }

    public bool CanManageUsers { get; private set; }

    public bool CanDeleteUsers { get; private set; }

    public bool CanAssignRoles { get; private set; }

    public bool CanManuallyConfirmEmail { get; private set; }

    private HashSet<Guid> ManageableRoleIds { get; set; } = [];

    private Guid? ActorUserId { get; set; }

    private bool AuthorizationLoaded { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public IdentityUserStatus Status { get; set; } =
        IdentityUserStatus.Any;

    /// <summary>Key of a declared <see cref="HelloAdminUserBadge"/> the list is filtered by.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Badge { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTimeOffset? CursorCreatedAt { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? CursorId { get; set; }

    /// <summary>The user whose management dialog the page opens.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? Manage { get; set; }

    public PendingAdminAction? PendingAction { get; private set; }

    public PendingAdminRoleAction? PendingRoleAction { get; private set; }

    /// <summary>A result explained from the current state, shown like a status message.</summary>
    public string? OutcomeMessage { get; private set; }

    internal HelloAdminFeedback? Feedback { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? PendingActionStateToken { get; set; }

    [TempData]
    public string? PendingRoleActionStateToken { get; set; }

    [TempData]
    public string? UsersFeedbackStateToken { get; set; }

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
        RestorePendingActions();
        RestoreFeedback();
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
        ClearFeedback();
        Manage = userId;
        var values = new Dictionary<string, string?>
        {
            ["target"] = userId.ToString("D"),
            ["reason"] = reason,
            ["blockedUntil"] = Request.Form["blockedUntil"].ToString(),
        };
        if (!HelloAdminSecurity.TryParseActionSlug(
                action,
                out var parsedAction))
        {
            ModelState.AddModelError(
                string.Empty,
                text["Admin.Errors.InvalidUserAction"]);
            return RedirectWithFeedback(values);
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
            ReplacePendingAction(new PendingAdminAction(
                userId,
                parsedAction,
                parameters,
                result.Value.ChallengeId,
                result.Value.ExpiresAt,
                result.Value.DeliveryChannel));
            return RedirectToList();
        }

        HelloAdminErrorText.AddErrors(ModelState, result.Errors, text);
        return RedirectWithFeedback(values);
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
        ClearFeedback();
        Manage = userId;
        if (!HelloAdminSecurity.TryParseActionSlug(
                action,
                out var parsedAction))
        {
            ModelState.AddModelError(
                string.Empty,
                text["Admin.Errors.InvalidUserAction"]);
            return RedirectWithFeedback();
        }

        if (!await IsAuthorizedAsync(options.ReadPolicyName)
            || !await IsAuthorizedAsync(GetPolicy(parsedAction)))
        {
            return Forbid();
        }

        var accessToken = await ReadAccessTokenAsync();
        if (accessToken is null || CurrentActorId() is not { } actorId)
        {
            return Challenge(HelloUiDefaults.AuthenticationScheme);
        }

        var parameters = new HelloAdminUserActionParameters(
            expectedVersion,
            blockedUntil,
            reason);
        var pending = new PendingAdminAction(
            userId,
            parsedAction,
            parameters,
            challengeId,
            ExpiresAt: null,
            DeliveryChannel: null);
        var clientKey = requestContext.CreateClientKey(HttpContext);
        var actionText = text[GetActionTextKey(parsedAction)];
        // The confirmation runs to completion even when the browser abandons
        // this request for a duplicate submission, which then shares the result.
        var outcome = await completionGuard.RunAsync(
            actorId,
            challengeId,
            $"user:{action}:{userId:N}",
            async () =>
            {
                var result = await application.CompleteUserActionAsync(
                    new HelloAdminCompleteUserActionCommand(
                        accessToken,
                        userId,
                        parsedAction,
                        parameters,
                        challengeId,
                        verificationCode,
                        clientKey),
                    CancellationToken.None);
                return ToOutcome(
                    result.IsSuccess,
                    result.Errors,
                    text["Admin.Common.ActionCompleted", actionText],
                    text);
            });
        return ApplyOutcome(
            outcome,
            pendingUser: pending,
            pendingRole: null,
            new HelloAdminOutcomeCheck(action, userId, null, actionText));
    }

    public async Task<IActionResult> OnPostBeginRoleActionAsync(
        Guid userId,
        Guid roleId,
        string action,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders();
        ClearFeedback();
        Manage = userId;
        if (!HelloAdminSecurity.TryParseRoleActionSlug(
                action,
                out var parsedAction)
            || parsedAction is not HelloAdminRoleAction.Assign
                and not HelloAdminRoleAction.Remove)
        {
            ModelState.AddModelError(
                string.Empty,
                text["Admin.Errors.InvalidRoleMembershipAction"]);
            return RedirectWithFeedback();
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
            ReplacePendingRoleAction(new PendingAdminRoleAction(
                parsedAction,
                roleId,
                userId,
                parameters,
                result.Value.ChallengeId,
                result.Value.ExpiresAt,
                result.Value.DeliveryChannel));
            return RedirectToList();
        }

        HelloAdminErrorText.AddErrors(ModelState, result.Errors, text);
        return RedirectWithFeedback();
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
        ClearFeedback();
        Manage = userId;
        if (!HelloAdminSecurity.TryParseRoleActionSlug(
                action,
                out var parsedAction)
            || parsedAction is not HelloAdminRoleAction.Assign
                and not HelloAdminRoleAction.Remove)
        {
            ModelState.AddModelError(
                string.Empty,
                text["Admin.Errors.InvalidRoleMembershipAction"]);
            return RedirectWithFeedback();
        }

        if (!await IsAuthorizedAsync(
                options.RoleAssignmentPolicyName))
        {
            return Forbid();
        }

        var accessToken = await ReadAccessTokenAsync();
        if (accessToken is null || CurrentActorId() is not { } actorId)
        {
            return Challenge(HelloUiDefaults.AuthenticationScheme);
        }

        var actionText = text[GetRoleActionTextKey(parsedAction)];
        var outcome = await HelloAdminRoleMembershipCompletion.CompleteAsync(
            completionGuard,
            roleApplication,
            text,
            actorId,
            accessToken,
            parsedAction,
            roleId,
            userId,
            challengeId,
            verificationCode,
            requestContext.CreateClientKey(HttpContext));
        var outcomeResult = ApplyOutcome(
            outcome,
            pendingUser: null,
            pendingRole: new PendingAdminRoleAction(
                parsedAction,
                roleId,
                userId,
                new HelloAdminRoleActionParameters(),
                challengeId,
                ExpiresAt: null,
                DeliveryChannel: null),
            new HelloAdminOutcomeCheck(action, userId, roleId, actionText));
        if (outcome.SignOut)
        {
            await ClearLocalSessionAsync();
        }

        return outcomeResult;
    }

    private RedirectToPageResult ApplyOutcome(
        HelloAdminCompletionOutcome outcome,
        PendingAdminAction? pendingUser,
        PendingAdminRoleAction? pendingRole,
        HelloAdminOutcomeCheck check)
    {
        if (outcome.SignOut)
        {
            ClearPending(pendingUser is not null);
            return RedirectToPage(
                "/SkopkaHello/Login",
                new RouteValueDictionary { [outcome.LoginFlag ?? "rolesChanged"] = true });
        }

        if (outcome.Completed)
        {
            ClearPending(pendingUser is not null);
            StatusMessage = outcome.StatusMessage;
            return RedirectToList();
        }

        foreach (var (key, message) in outcome.Errors)
        {
            ModelState.AddModelError(key, message);
        }

        if (outcome.KeepPending)
        {
            // A wrong code can be retried with the same confirmation.
            if (pendingUser is not null)
            {
                ReplacePendingAction(RestoredPendingAction(pendingUser));
            }
            else if (pendingRole is not null)
            {
                ReplacePendingRoleAction(RestoredPendingRoleAction(pendingRole));
            }

            return RedirectWithFeedback();
        }

        ClearPending(pendingUser is not null);
        return RedirectWithFeedback(check: outcome.CheckState ? check : null);
    }

    internal static HelloAdminCompletionOutcome ToOutcome(
        bool succeeded,
        IReadOnlyCollection<Error> errors,
        string completedMessage,
        IHelloUiLocalizer? text = null)
    {
        if (succeeded)
        {
            return new(true, completedMessage, []);
        }

        var state = new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary();
        if (text is not null)
        {
            HelloAdminErrorText.AddErrors(state, errors, text);
        }

        var captured = HelloAdminFeedback.Capture(state);
        if (HelloAdminErrorText.HasCode(errors, HelloAdminErrorCodes.SessionCleanupRequired))
        {
            // The mutation committed; only the follow-up session cleanup failed.
            // Report it, but never invite the person to repeat the action.
            return new(true, string.Join(" ", captured.Select(item => item.Value)), []);
        }

        var restart = HelloAdminErrorText.HasCode(errors, HelloAdminErrorCodes.RestartRequired);
        return new(false, null, captured, KeepPending: !restart, CheckState: restart);
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
            HelloAdminUserAction.ConfirmEmail => "Confirm email",
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
            HelloAdminUserAction.ConfirmEmail =>
                "Admin.Users.ConfirmEmail",
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

    /// <summary>Whether the page opens the management dialog of <paramref name="userId"/>.</summary>
    public bool ShouldOpen(Guid userId)
        => Manage == userId && PendingAction is null && PendingRoleAction is null;

    /// <summary>A value typed into a form of <paramref name="userId"/> before the last failed submission.</summary>
    public string? PostedValue(Guid userId, string name)
        => Feedback?.Value(userId.ToString("D"), name);

    public IReadOnlyList<HelloAdminUserBadge> BadgesOf(Guid userId)
        => UserBadges.TryGetValue(userId, out var badges) ? badges : [];

    public string BadgeLabel(HelloAdminUserBadge badge)
        => text.TryGetString($"Admin.Badges.{badge.Key}", out var label)
            ? label
            : badge.Label;

    /// <summary>The list state that every form and redirect of this page carries.</summary>
    public IReadOnlyDictionary<string, string?> ListContext(Guid? manage)
        => new Dictionary<string, string?>
        {
            [nameof(Search)] = Search,
            [nameof(Status)] = Status == IdentityUserStatus.Any ? null : Status.ToString(),
            [nameof(Badge)] = Badge,
            [nameof(CursorCreatedAt)] = CursorCreatedAt?.ToString("O", CultureInfo.InvariantCulture),
            [nameof(CursorId)] = CursorId?.ToString("D"),
            [nameof(Manage)] = manage?.ToString("D"),
        };

    private RedirectToPageResult RedirectToList()
    {
        var values = new RouteValueDictionary();
        foreach (var (key, value) in ListContext(Manage))
        {
            if (!string.IsNullOrEmpty(value))
            {
                values[key] = value;
            }
        }

        return RedirectToPage("/SkopkaHelloAdmin/Users", values);
    }

    private RedirectToPageResult RedirectWithFeedback(
        IReadOnlyDictionary<string, string?>? values = null,
        HelloAdminOutcomeCheck? check = null)
    {
        var feedback = HelloAdminFeedback.From(ModelState, values, check);
        if (feedback.Errors.Count > 0 || feedback.Check is not null)
        {
            UsersFeedbackStateToken = prgState.Store(feedback);
        }

        ModelState.Clear();
        return RedirectToList();
    }

    private void ClearFeedback()
    {
        prgState.Remove(UsersFeedbackStateToken);
        UsersFeedbackStateToken = null;
    }

    private void ClearPending(bool userAction)
    {
        if (userAction)
        {
            prgState.Remove(PendingActionStateToken);
            PendingActionStateToken = null;
        }
        else
        {
            prgState.Remove(PendingRoleActionStateToken);
            PendingRoleActionStateToken = null;
        }
    }

    private void ReplacePendingAction(PendingAdminAction pending)
    {
        prgState.Remove(PendingActionStateToken);
        prgState.Remove(PendingRoleActionStateToken);
        PendingRoleActionStateToken = null;
        PendingActionStateToken = prgState.Store(pending);
    }

    private void ReplacePendingRoleAction(PendingAdminRoleAction pending)
    {
        prgState.Remove(PendingRoleActionStateToken);
        prgState.Remove(PendingActionStateToken);
        PendingActionStateToken = null;
        PendingRoleActionStateToken = prgState.Store(pending);
    }

    private PendingAdminAction RestoredPendingAction(PendingAdminAction submitted)
        => prgState.TryGet<PendingAdminAction>(PendingActionStateToken, out var stored)
            && stored is not null
            && stored.ChallengeId == submitted.ChallengeId
                ? stored
                : submitted;

    private PendingAdminRoleAction RestoredPendingRoleAction(PendingAdminRoleAction submitted)
        => prgState.TryGet<PendingAdminRoleAction>(PendingRoleActionStateToken, out var stored)
            && stored is not null
            && stored.ChallengeId == submitted.ChallengeId
                ? stored
                : submitted;

    private async Task LoadUsersAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        await LoadAuthorizationAsync();
        if (Badge is not null && FilterBadges.All(badge => badge.Key != Badge))
        {
            Badge = null;
        }

        var result = await directory.QueryAsync(
            new HelloAdminDirectoryQuery(
                accessToken,
                Search,
                Status,
                CursorCreatedAt is not null && CursorId is not null
                    ? new IdentityUserCursor(
                        CursorCreatedAt.Value,
                        CursorId.Value)
                    : null,
                PageSize,
                Badge),
            cancellationToken);
        if (!result.IsSuccess)
        {
            HelloAdminErrorText.AddErrors(ModelState, result.Errors, text);
            return;
        }

        Users = result.Value.Rows.Select(row => row.User).ToArray();
        NextCursor = result.Value.NextCursor;
        ScanLimitReached = result.Value.LimitReached;
        ScannedUsers = result.Value.Examined;
        FilteredTotal = result.Value.Total;
        UserRoles = result.Value.Rows.ToDictionary(row => row.User.Id, row => row.Roles);
        UserBadges = result.Value.Rows.ToDictionary(row => row.User.Id, row => row.Badges);
        var roleCatalog = await roleApplication.QueryRolesAsync(
            new HelloAdminQueryRolesCommand(
                accessToken,
                PageSize: RoleCatalogPageSize),
            cancellationToken);
        if (!roleCatalog.IsSuccess)
        {
            HelloAdminErrorText.AddErrors(ModelState, roleCatalog.Errors, text);
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
            HelloAdminErrorText.AddErrors(ModelState, actorRoles.Errors, text);
            return;
        }

        var knownRoles = roleCatalog.Value.Items
            .Concat(UserRoles.Values.SelectMany(
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

    private Guid? CurrentActorId()
        => Guid.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub"),
            out var id)
                ? id
                : null;

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
        CanManuallyConfirmEmail = CanManageUsers
            && options.ManualEmailConfirmationEnabled;
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

    private void ApplySensitiveResponseHeaders()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }

    private void RestorePendingActions()
    {
        if (prgState.TryGet<PendingAdminAction>(
                PendingActionStateToken,
                out var pendingAction)
            && pendingAction is not null)
        {
            PendingAction = pendingAction;
            TempData.Keep(nameof(PendingActionStateToken));
        }

        if (prgState.TryGet<PendingAdminRoleAction>(
                PendingRoleActionStateToken,
                out var pendingRoleAction)
            && pendingRoleAction is not null)
        {
            PendingRoleAction = pendingRoleAction;
            TempData.Keep(nameof(PendingRoleActionStateToken));
        }
    }

    private void RestoreFeedback()
    {
        if (!prgState.TryGet<HelloAdminFeedback>(
                UsersFeedbackStateToken,
                out var feedback)
            || feedback is null)
        {
            return;
        }

        // Errors stay visible on refresh until the next submission.
        TempData.Keep(nameof(UsersFeedbackStateToken));
        Feedback = feedback;
        if (feedback.Check is { } check
            && ExplainOutcome(check) is { } explanation)
        {
            OutcomeMessage = explanation;
            return;
        }

        feedback.Apply(ModelState);
    }

    /// <summary>
    /// After a consumed or expired confirmation, says that the intended result
    /// is already in effect when the current state shows it.
    /// </summary>
    private string? ExplainOutcome(HelloAdminOutcomeCheck check)
    {
        if (check.UserId is not { } userId
            || Users.FirstOrDefault(user => user.Id == userId) is not { } user)
        {
            return null;
        }

        if (check.RoleId is { } roleId)
        {
            var assigned = UserRoles.TryGetValue(userId, out var roles)
                && roles.Any(role => role.Id == roleId);
            var roleName = (roles ?? []).FirstOrDefault(role => role.Id == roleId)?.Name
                ?? RoleCatalog.FirstOrDefault(role => role.Id == roleId)?.Name
                ?? roleId.ToString("D");
            return check.Action switch
            {
                "assign" when assigned => text["Admin.Outcome.RoleAlreadyAssigned", roleName].Value,
                "remove" when !assigned => text["Admin.Outcome.RoleAlreadyRemoved", roleName].Value,
                _ => null,
            };
        }

        var blocked = user.BlockedAt is not null
            && (user.BlockedUntil is null || user.BlockedUntil > DateTimeOffset.UtcNow);
        var inEffect = check.Action switch
        {
            "block" => blocked,
            "unblock" => !blocked && user.DeletedAt is null,
            "delete" => user.DeletedAt is not null,
            "restore" => user.DeletedAt is null,
            "confirm-email" => user.EmailConfirmed,
            _ => false,
        };
        return inEffect
            ? text["Admin.Outcome.AlreadyInEffect", check.ActionText].Value
            : null;
    }
}

public sealed record PendingAdminAction(
    Guid UserId,
    HelloAdminUserAction Action,
    HelloAdminUserActionParameters Parameters,
    Guid ChallengeId,
    DateTimeOffset? ExpiresAt,
    HelloDeliveryChannel? DeliveryChannel);
