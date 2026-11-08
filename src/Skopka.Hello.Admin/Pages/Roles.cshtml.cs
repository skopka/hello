using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Hello.UI;
using Skopka.Identity.Roles;
using Skopka.Identity.Roles.Queries;
using Skopka.Identity.Users.Queries;

namespace Skopka.Hello.Admin.Pages;

[Authorize(Policy = HelloUiDefaults.AuthorizationPolicy)]
public sealed class RolesModel : PageModel
{
    public const int MemberPageSize = 25;

    public const int CandidatePageSize = 10;

    private readonly IHelloAdminRoleApplication application;
    private readonly IHelloRequestContext requestContext;
    private readonly IAuthorizationService authorization;
    private readonly HelloUiPrgStateStore prgState;
    private readonly SkopkaHelloAdminOptions options;
    private readonly IHelloUiLocalizer text;
    private readonly IHelloSessionCookieManager sessionCookies;
    private readonly HelloAdminRoleRulesEvaluator roleRules;

    public RolesModel(
        IHelloAdminRoleApplication application,
        IHelloRequestContext requestContext,
        IAuthorizationService authorization,
        HelloUiPrgStateStore prgState,
        SkopkaHelloAdminOptions options,
        IHelloUiLocalizer text,
        IHelloSessionCookieManager sessionCookies)
    {
        this.application = application;
        this.requestContext = requestContext;
        this.authorization = authorization;
        this.prgState = prgState;
        this.options = options;
        this.text = text;
        this.sessionCookies = sessionCookies;
        roleRules = new(options);
    }

    private HelloAdminUserDirectory directory
        => HttpContext.RequestServices.GetRequiredService<HelloAdminUserDirectory>();

    private HelloAdminCompletionGuard completionGuard
        => HttpContext.RequestServices.GetRequiredService<HelloAdminCompletionGuard>();

    public IReadOnlyList<IdentityRole> Roles { get; private set; } = [];

    public IdentityRoleCursor? NextCursor { get; private set; }

    public bool RoleManagementEnabled => options.RoleManagementEnabled;

    public bool CanReadRoles { get; private set; }

    public bool CanEditRoles { get; private set; }

    public bool CanAssignRoles { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTimeOffset? CursorCreatedAt { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? CursorId { get; set; }

    /// <summary>The role whose dialog, with its members, the page opens.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? Open { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? MemberSearch { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTimeOffset? MemberCursorCreatedAt { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? MemberCursorId { get; set; }

    /// <summary>Search for accounts to add to the open role.</summary>
    [BindProperty(SupportsGet = true)]
    public string? AddSearch { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTimeOffset? AddCursorCreatedAt { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? AddCursorId { get; set; }

    public IdentityRole? OpenRole { get; private set; }

    internal IReadOnlyList<HelloAdminDirectoryRow> Members { get; private set; } = [];

    public IdentityUserCursor? MembersNextCursor { get; private set; }

    /// <summary>Number of members matching <see cref="MemberSearch"/>, counted by Identity.</summary>
    public long? MembersTotal { get; private set; }

    /// <summary>
    /// One page of the account search for adding members. Accounts already in
    /// the role stay in the list, marked; deleted accounts are left out.
    /// </summary>
    internal IReadOnlyList<HelloAdminDirectoryRow>? Candidates { get; private set; }

    public IdentityUserCursor? CandidatesNextCursor { get; private set; }

    internal bool InOpenRole(HelloAdminDirectoryRow row)
        => OpenRole is { } role && row.Roles.Any(item => item.Id == role.Id);

    /// <summary>Whether the current administrator may add accounts to and remove them from the open role.</summary>
    public bool CanManageOpenRoleMembers { get; private set; }

    public PendingAdminRoleAction? PendingAction { get; private set; }

    /// <summary>Who a pending membership change is for.</summary>
    public string? PendingTargetLabel { get; private set; }

    public string? OutcomeMessage { get; private set; }

    internal HelloAdminFeedback? Feedback { get; private set; }

    private Guid? ActorUserId { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? PendingActionStateToken { get; set; }

    [TempData]
    public string? RolesFeedbackStateToken { get; set; }

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders();
        await LoadAuthorizationAsync();
        // The role catalog and its members are read access. A delegated role
        // assigner without it manages membership from the Users page.
        if (!CanReadRoles)
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

        if ((MemberCursorCreatedAt is null) != (MemberCursorId is null))
        {
            MemberCursorCreatedAt = null;
            MemberCursorId = null;
        }

        var accessToken = await ReadAccessTokenAsync();
        if (accessToken is null)
        {
            return Challenge(HelloUiDefaults.AuthenticationScheme);
        }

        await LoadRolesAsync(accessToken, cancellationToken);
        RestorePendingAction();
        await LoadOpenRoleAsync(accessToken, cancellationToken);
        RestoreFeedback();
        return Page();
    }

    public async Task<IActionResult> OnPostBeginActionAsync(
        string action,
        Guid? roleId,
        Guid? targetUserId,
        long? expectedVersion,
        string? name,
        string? description,
        Guid? parentId,
        string? targetLabel,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders();
        ClearFeedback();
        Open = roleId ?? Open;
        var values = new Dictionary<string, string?>
        {
            ["target"] = FeedbackTarget(action, roleId),
            ["name"] = name,
            ["description"] = description,
            ["parentId"] = Request.Form["parentId"].ToString(),
        };
        if (!HelloAdminSecurity.TryParseRoleActionSlug(
                action,
                out var parsedAction))
        {
            ModelState.AddModelError(
                string.Empty,
                text["Admin.Errors.InvalidRoleAction"]);
            return RedirectWithFeedback(values);
        }

        if (!await IsAuthorizedForAsync(parsedAction))
        {
            return Forbid();
        }

        var accessToken = await ReadAccessTokenAsync();
        if (accessToken is null)
        {
            return Challenge(HelloUiDefaults.AuthenticationScheme);
        }

        var parameters = new HelloAdminRoleActionParameters(
            expectedVersion,
            name,
            description,
            parentId);
        var result = await application.BeginRoleActionAsync(
            new HelloAdminBeginRoleActionCommand(
                accessToken,
                parsedAction,
                roleId,
                targetUserId,
                parameters,
                requestContext.CreateClientKey(HttpContext)),
            cancellationToken);
        if (result.IsSuccess)
        {
            ReplacePending(new PendingRoleState(
                new PendingAdminRoleAction(
                    parsedAction,
                    roleId,
                    targetUserId,
                    parameters,
                    result.Value.ChallengeId,
                    result.Value.ExpiresAt,
                    result.Value.DeliveryChannel),
                IsMembership(parsedAction) ? Trim(targetLabel) : null));
            return RedirectToList();
        }

        HelloAdminErrorText.AddErrors(ModelState, result.Errors, text);
        return RedirectWithFeedback(values);
    }

    public async Task<IActionResult> OnPostCompleteActionAsync(
        string action,
        Guid? roleId,
        Guid? targetUserId,
        long? expectedVersion,
        string? name,
        string? description,
        Guid? parentId,
        Guid challengeId,
        string verificationCode,
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders();
        ClearFeedback();
        Open = roleId ?? Open;
        if (!HelloAdminSecurity.TryParseRoleActionSlug(
                action,
                out var parsedAction))
        {
            ModelState.AddModelError(
                string.Empty,
                text["Admin.Errors.InvalidRoleAction"]);
            return RedirectWithFeedback();
        }

        if (!await IsAuthorizedForAsync(parsedAction))
        {
            return Forbid();
        }

        var accessToken = await ReadAccessTokenAsync();
        if (accessToken is null || CurrentActorId() is not { } actorId)
        {
            return Challenge(HelloUiDefaults.AuthenticationScheme);
        }

        var parameters = new HelloAdminRoleActionParameters(
            expectedVersion,
            name,
            description,
            parentId);
        var clientKey = requestContext.CreateClientKey(HttpContext);
        var actionText = text[GetActionTextKey(parsedAction)].Value;
        HelloAdminCompletionOutcome outcome;
        if (IsMembership(parsedAction) && roleId is { } memberRoleId && targetUserId is { } memberId)
        {
            outcome = await HelloAdminRoleMembershipCompletion.CompleteAsync(
                completionGuard,
                application,
                text,
                actorId,
                accessToken,
                parsedAction,
                memberRoleId,
                memberId,
                challengeId,
                verificationCode,
                clientKey);
        }
        else
        {
            outcome = await completionGuard.RunAsync(
                actorId,
                challengeId,
                $"role:{action}:{roleId:N}",
                async () =>
                {
                    // Runs to completion even when the browser abandons this
                    // request for a duplicate submission.
                    var result = await application.CompleteRoleActionAsync(
                        new HelloAdminCompleteRoleActionCommand(
                            accessToken,
                            parsedAction,
                            roleId,
                            targetUserId,
                            parameters,
                            challengeId,
                            verificationCode,
                            clientKey),
                        CancellationToken.None);
                    return UsersModel.ToOutcome(
                        result.IsSuccess,
                        result.Errors,
                        text["Admin.Common.ActionCompleted", actionText],
                        text) with
                    {
                        EntityId = result.IsSuccess ? result.Value.Role?.Id : null,
                    };
                });
        }

        if (outcome.SignOut)
        {
            ClearPending();
            await ClearLocalSessionAsync();
            return RedirectToPage(
                "/SkopkaHello/Login",
                new RouteValueDictionary { [outcome.LoginFlag ?? "rolesChanged"] = true });
        }

        if (outcome.Completed)
        {
            ClearPending();
            StatusMessage = outcome.StatusMessage;
            Open = parsedAction switch
            {
                HelloAdminRoleAction.Delete => null,
                HelloAdminRoleAction.Create => outcome.EntityId,
                _ => Open,
            };
            return RedirectToList();
        }

        foreach (var (key, message) in outcome.Errors)
        {
            ModelState.AddModelError(key, message);
        }

        if (outcome.KeepPending)
        {
            if (prgState.TryGet<PendingRoleState>(PendingActionStateToken, out var stored)
                && stored is not null
                && stored.Action.ChallengeId == challengeId)
            {
                ReplacePending(stored);
            }
            else
            {
                ReplacePending(new PendingRoleState(
                    new PendingAdminRoleAction(
                        parsedAction,
                        roleId,
                        targetUserId,
                        parameters,
                        challengeId,
                        ExpiresAt: null,
                        DeliveryChannel: null),
                    null));
            }

            return RedirectWithFeedback();
        }

        ClearPending();
        return RedirectWithFeedback(
            check: outcome.CheckState
                ? new HelloAdminOutcomeCheck(action, targetUserId, roleId, actionText)
                : null);
    }

    public static string GetActionSlug(HelloAdminRoleAction action)
        => HelloAdminSecurity.GetActionSlug(action);

    public static string GetActionLabel(HelloAdminRoleAction action)
        => action switch
        {
            HelloAdminRoleAction.Create => "Create role",
            HelloAdminRoleAction.Update => "Update role",
            HelloAdminRoleAction.Delete => "Delete role",
            HelloAdminRoleAction.Assign => "Assign role",
            HelloAdminRoleAction.Remove => "Remove role",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

    public static string GetActionTextKey(HelloAdminRoleAction action)
        => action switch
        {
            HelloAdminRoleAction.Create => "Admin.Roles.Create",
            HelloAdminRoleAction.Update => "Admin.Roles.Update",
            HelloAdminRoleAction.Delete => "Admin.Roles.Delete",
            HelloAdminRoleAction.Assign => "Admin.Roles.Assign",
            HelloAdminRoleAction.Remove => "Admin.Roles.Remove",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

    /// <summary>Whether the page opens the dialog of <paramref name="roleId"/>.</summary>
    public bool ShouldOpen(Guid roleId)
        => Open == roleId && PendingAction is null;

    /// <summary>Whether the page reopens the new-role dialog after a failed submission.</summary>
    public bool ShouldOpenCreate
        => PendingAction is null && Feedback?.Errors.Count > 0
            && Feedback.Values.TryGetValue("target", out var target) && target == "create";

    public string? PostedValue(Guid? roleId, string name)
        => Feedback?.Value(roleId?.ToString("D") ?? "create", name);

    public bool CanRemoveMember(Guid userId)
        => CanManageOpenRoleMembers
            && OpenRole is { } role
            && (ActorUserId != userId
                || roleRules.GetProtection(role.Name) is not (
                    HelloRoleProtection.System
                        or HelloRoleProtection.Retained));

    public string BadgeLabel(HelloAdminUserBadge badge)
        => text.TryGetString($"Admin.Badges.{badge.Key}", out var label)
            ? label
            : badge.Label;

    public static string UserLabel(HelloAdminUser user)
        => user.UserName ?? user.Email ?? user.Phone ?? user.Id.ToString("D");

    /// <summary>The list state that every form and redirect of this page carries.</summary>
    public IReadOnlyDictionary<string, string?> ListContext(Guid? open, bool members = true)
        => new Dictionary<string, string?>
        {
            [nameof(Search)] = Search,
            [nameof(CursorCreatedAt)] = CursorCreatedAt?.ToString("O", CultureInfo.InvariantCulture),
            [nameof(CursorId)] = CursorId?.ToString("D"),
            [nameof(Open)] = open?.ToString("D"),
            [nameof(MemberSearch)] = members && open == Open ? MemberSearch : null,
            [nameof(MemberCursorCreatedAt)] = members && open == Open
                ? MemberCursorCreatedAt?.ToString("O", CultureInfo.InvariantCulture)
                : null,
            [nameof(MemberCursorId)] = members && open == Open ? MemberCursorId?.ToString("D") : null,
            [nameof(AddSearch)] = members && open == Open ? AddSearch : null,
            [nameof(AddCursorCreatedAt)] = members && open == Open
                ? AddCursorCreatedAt?.ToString("O", CultureInfo.InvariantCulture)
                : null,
            [nameof(AddCursorId)] = members && open == Open ? AddCursorId?.ToString("D") : null,
        };

    private static bool IsMembership(HelloAdminRoleAction action)
        => action is HelloAdminRoleAction.Assign or HelloAdminRoleAction.Remove;

    private static string? FeedbackTarget(string action, Guid? roleId)
        => string.Equals(action, "create", StringComparison.Ordinal)
            ? "create"
            : roleId?.ToString("D");

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim() is { Length: > 256 } long_ ? long_[..256] : value.Trim();

    private async Task<bool> IsAuthorizedForAsync(HelloAdminRoleAction action)
        => IsMembership(action)
            // Membership follows the same rules as on the Users page.
            ? await IsAuthorizedAsync(options.ReadPolicyName)
                && await IsAuthorizedAsync(options.RoleAssignmentPolicyName)
            : await IsAuthorizedAsync(options.ReadPolicyName)
                && await IsAuthorizedAsync(options.DeletePolicyName);

    private RedirectToPageResult RedirectToList()
    {
        var values = new RouteValueDictionary();
        foreach (var (key, value) in ListContext(Open))
        {
            if (!string.IsNullOrEmpty(value))
            {
                values[key] = value;
            }
        }

        return RedirectToPage("/SkopkaHelloAdmin/Roles", values);
    }

    private RedirectToPageResult RedirectWithFeedback(
        IReadOnlyDictionary<string, string?>? values = null,
        HelloAdminOutcomeCheck? check = null)
    {
        var feedback = HelloAdminFeedback.From(ModelState, values, check);
        if (feedback.Errors.Count > 0 || feedback.Check is not null)
        {
            RolesFeedbackStateToken = prgState.Store(feedback);
        }

        ModelState.Clear();
        return RedirectToList();
    }

    private void ClearFeedback()
    {
        prgState.Remove(RolesFeedbackStateToken);
        RolesFeedbackStateToken = null;
    }

    private void ClearPending()
    {
        prgState.Remove(PendingActionStateToken);
        PendingActionStateToken = null;
    }

    private void ReplacePending(PendingRoleState pending)
    {
        prgState.Remove(PendingActionStateToken);
        PendingActionStateToken = prgState.Store(pending);
    }

    private async Task LoadRolesAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var result = await application.QueryRolesAsync(
            new HelloAdminQueryRolesCommand(
                accessToken,
                Search,
                PageSize: 50,
                Cursor: CursorCreatedAt is not null && CursorId is not null
                    ? new IdentityRoleCursor(
                        CursorCreatedAt.Value,
                        CursorId.Value)
                    : null),
            cancellationToken);
        if (result.IsSuccess)
        {
            Roles = result.Value.Items;
            NextCursor = result.Value.NextCursor;
            return;
        }

        HelloAdminErrorText.AddErrors(ModelState, result.Errors, text);
    }

    private async Task LoadOpenRoleAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        OpenRole = Open is { } open
            ? Roles.FirstOrDefault(role => role.Id == open)
            : null;
        if (OpenRole is null)
        {
            return;
        }

        ActorUserId = CurrentActorId();
        if (CanAssignRoles && ActorUserId is { } actorId)
        {
            var actorRoles = await application.GetUserRolesAsync(
                new HelloAdminGetUserRolesCommand(accessToken, actorId),
                cancellationToken);
            CanManageOpenRoleMembers = actorRoles.IsSuccess
                && roleRules.CanManageMembership(actorRoles.Value, OpenRole);
        }

        var members = await directory.QueryAsync(
            new HelloAdminDirectoryQuery(
                accessToken,
                MemberSearch,
                IdentityUserStatus.Any,
                MemberCursorCreatedAt is not null && MemberCursorId is not null
                    ? new IdentityUserCursor(MemberCursorCreatedAt.Value, MemberCursorId.Value)
                    : null,
                MemberPageSize,
                MemberOfRoleId: OpenRole.Id),
            cancellationToken);
        if (!members.IsSuccess)
        {
            HelloAdminErrorText.AddErrors(ModelState, members.Errors, text);
            return;
        }

        Members = members.Value.Rows;
        MembersNextCursor = members.Value.NextCursor;
        MembersTotal = members.Value.Total;
        if (!CanManageOpenRoleMembers || string.IsNullOrWhiteSpace(AddSearch))
        {
            return;
        }

        var candidates = await directory.QueryAsync(
            new HelloAdminDirectoryQuery(
                accessToken,
                AddSearch,
                IdentityUserStatus.Any,
                AddCursorCreatedAt is not null && AddCursorId is not null
                    ? new IdentityUserCursor(AddCursorCreatedAt.Value, AddCursorId.Value)
                    : null,
                CandidatePageSize),
            cancellationToken);
        if (!candidates.IsSuccess)
        {
            HelloAdminErrorText.AddErrors(ModelState, candidates.Errors, text);
            return;
        }

        Candidates = candidates.Value.Rows.Where(row => row.User.DeletedAt is null).ToArray();
        CandidatesNextCursor = candidates.Value.NextCursor;
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

    private async Task LoadAuthorizationAsync()
    {
        CanReadRoles = await IsAuthorizedAsync(options.ReadPolicyName);
        CanEditRoles = CanReadRoles
            && options.RoleManagementEnabled
            && await IsAuthorizedAsync(options.DeletePolicyName);
        CanAssignRoles = await IsAuthorizedAsync(options.RoleAssignmentPolicyName);
    }

    private async Task<bool> IsAuthorizedAsync(string policyName)
        => (await authorization.AuthorizeAsync(
            User,
            HttpContext,
            policyName)).Succeeded;

    private async Task ClearLocalSessionAsync()
    {
        sessionCookies.DeleteSessionCookies(HttpContext);
        await HttpContext.SignOutAsync(HelloUiDefaults.AuthenticationScheme);
    }

    private void ApplySensitiveResponseHeaders()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }

    private void RestorePendingAction()
    {
        if (!prgState.TryGet<PendingRoleState>(
                PendingActionStateToken,
                out var pending)
            || pending is null)
        {
            return;
        }

        PendingAction = pending.Action;
        PendingTargetLabel = pending.TargetLabel;
        TempData.Keep(nameof(PendingActionStateToken));
    }

    private void RestoreFeedback()
    {
        if (!prgState.TryGet<HelloAdminFeedback>(
                RolesFeedbackStateToken,
                out var feedback)
            || feedback is null)
        {
            return;
        }

        // Errors stay visible on refresh until the next submission.
        TempData.Keep(nameof(RolesFeedbackStateToken));
        Feedback = feedback;
        if (feedback.Check is { } check && ExplainOutcome(check) is { } explanation)
        {
            OutcomeMessage = explanation;
            return;
        }

        feedback.Apply(ModelState);
    }

    private string? ExplainOutcome(HelloAdminOutcomeCheck check)
    {
        if (check.RoleId is not { } roleId)
        {
            return null;
        }

        var role = Roles.FirstOrDefault(item => item.Id == roleId);
        if (check.Action == "delete")
        {
            return role is null && Search is null && CursorId is null
                ? text["Admin.Outcome.AlreadyInEffect", check.ActionText].Value
                : null;
        }

        if (check.UserId is not { } userId || OpenRole?.Id != roleId)
        {
            return null;
        }

        var member = Members.Any(row => row.User.Id == userId);
        return check.Action switch
        {
            "assign" when member => text["Admin.Outcome.RoleAlreadyAssigned", OpenRole.Name].Value,
            "remove" when !member && MemberSearch is null && MemberCursorId is null && MembersNextCursor is null
                => text["Admin.Outcome.RoleAlreadyRemoved", OpenRole.Name].Value,
            _ => null,
        };
    }

    private sealed record PendingRoleState(
        PendingAdminRoleAction Action,
        string? TargetLabel);
}

public sealed record PendingAdminRoleAction(
    HelloAdminRoleAction Action,
    Guid? RoleId,
    Guid? TargetUserId,
    HelloAdminRoleActionParameters Parameters,
    Guid ChallengeId,
    DateTimeOffset? ExpiresAt,
    HelloDeliveryChannel? DeliveryChannel);
