using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.UI;
using Skopka.Identity.Errors;
using Skopka.Identity.Roles;
using Skopka.Identity.Roles.Queries;

namespace Skopka.Hello.Admin.Pages;

[Authorize(Policy = HelloUiDefaults.AuthorizationPolicy)]
public sealed class RolesModel(
    IHelloAdminRoleApplication application,
    IHelloRequestContext requestContext,
    IAuthorizationService authorization,
    SkopkaHelloAdminOptions options,
    IHelloUiLocalizer text)
    : PageModel
{
    public IReadOnlyList<IdentityRole> Roles { get; private set; } = [];

    public IdentityRoleCursor? NextCursor { get; private set; }

    public bool RoleManagementEnabled => options.RoleManagementEnabled;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTimeOffset? CursorCreatedAt { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? CursorId { get; set; }

    public PendingAdminRoleAction? PendingAction { get; private set; }

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

        await LoadRolesAsync(accessToken, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders();
        if (!HelloAdminSecurity.TryParseRoleActionSlug(
                action,
                out var parsedAction))
        {
            ModelState.AddModelError(
                string.Empty,
                text["Admin.Errors.InvalidRoleAction"]);
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
            PendingAction = new PendingAdminRoleAction(
                parsedAction,
                roleId,
                targetUserId,
                parameters,
                result.Value.ChallengeId,
                result.Value.ExpiresAt,
                result.Value.DeliveryChannel);
        }
        else
        {
            AddErrors(result.Errors);
        }

        await LoadRolesAsync(accessToken, cancellationToken);
        return Page();
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
        if (!HelloAdminSecurity.TryParseRoleActionSlug(
                action,
                out var parsedAction))
        {
            ModelState.AddModelError(
                string.Empty,
                text["Admin.Errors.InvalidRoleAction"]);
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

        var parameters = new HelloAdminRoleActionParameters(
            expectedVersion,
            name,
            description,
            parentId);
        var result = await application.CompleteRoleActionAsync(
            new HelloAdminCompleteRoleActionCommand(
                accessToken,
                parsedAction,
                roleId,
                targetUserId,
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
                "/SkopkaHelloAdmin/Roles",
                new { Search });
        }

        AddErrors(result.Errors);
        PendingAction = new PendingAdminRoleAction(
            parsedAction,
            roleId,
            targetUserId,
            parameters,
            challengeId,
            ExpiresAt: null,
            DeliveryChannel: null);
        await LoadRolesAsync(accessToken, cancellationToken);
        return Page();
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

        await LoadRolesAsync(accessToken, cancellationToken);
        return Page();
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

    private void ApplySensitiveResponseHeaders()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}

public sealed record PendingAdminRoleAction(
    HelloAdminRoleAction Action,
    Guid? RoleId,
    Guid? TargetUserId,
    HelloAdminRoleActionParameters Parameters,
    Guid ChallengeId,
    DateTimeOffset? ExpiresAt,
    HelloDeliveryChannel? DeliveryChannel);
