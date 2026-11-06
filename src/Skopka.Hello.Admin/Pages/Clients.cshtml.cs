using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;
using Skopka.Hello.AuthorizationServer;
using Skopka.Hello.UI;

namespace Skopka.Hello.Admin.Pages;

[Authorize(Policy = HelloUiDefaults.AuthorizationPolicy)]
public sealed class ClientsModel(
    IHelloAdminClientApplication app,
    IHelloAuthorizationClientManagement clients,
    IHelloRequestContext context,
    IAuthorizationService authorization,
    HelloUiPrgStateStore prgState,
    SkopkaHelloAdminOptions options,
    IHelloUiLocalizer text) : PageModel
{
    public IReadOnlyList<HelloAuthorizationClientView> Clients { get; private set; } = [];
    public HelloAuthorizationClientCatalog Catalog => clients.GetCatalog();
    [BindProperty(SupportsGet = true)]
    public int Offset { get; set; }
    [BindProperty(SupportsGet = true)]
    public string? Edit { get; set; }
    public bool CanManage { get; private set; }
    public string? Secret { get; private set; }

    public string? StatusMessage { get; private set; }
    [TempData]
    public string? ClientsStatusMessageTempData { get; set; }
    [TempData]
    public string? ClientsPendingStateToken { get; set; }
    [TempData]
    public string? ClientsSecretStateToken { get; set; }
    public Guid? ChallengeId { get; private set; }
    [BindProperty]
    public string ActionName { get; set; } = "create";
    [BindProperty]
    public string? ClientId { get; set; }
    [BindProperty]
    public string? DisplayName { get; set; }
    [BindProperty]
    public HelloAuthorizationClientType Type { get; set; }
    [BindProperty]
    public string? Resource { get; set; }
    [BindProperty]
    public string? RedirectUris { get; set; }
    [BindProperty]
    public string? PostLogoutRedirectUris { get; set; }
    [BindProperty]
    public string[] SelectedScopes { get; set; } = [];
    [BindProperty]
    public bool HonorReauthenticationPrompts { get; set; } = true;
    [BindProperty]
    public long? ExpectedRevision { get; set; }
    [BindProperty]
    public string? VerificationCode { get; set; }
    private HelloAdminClientAction Action()
        => new(
            ActionName,
            new HelloAuthorizationClientInput(
                ClientId ?? string.Empty, DisplayName ?? string.Empty, Type, Resource ?? string.Empty,
                Lines(RedirectUris), Lines(PostLogoutRedirectUris),
                SelectedScopes, HonorReauthenticationPrompts),
            ExpectedRevision);

    private static string[] Lines(string? text)
        => (text ?? string.Empty).Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var page = await LoadAsync(ct);
        if (page is not PageResult)
        {
            return page;
        }
        StatusMessage = ClientsStatusMessageTempData;
        RestoreSecret();
        RestorePending();
        if (ChallengeId is not null || string.IsNullOrWhiteSpace(Edit))
        {
            return page;
        }
        if (!CanManage)
        {
            return Forbid();
        }
        var found = await clients.GetAsync(Edit, ct);
        if (!found.IsSuccess || found.Value.Ownership != "administration")
        {
            return NotFound();
        }
        var client = found.Value;
        ActionName = "update";
        ClientId = client.ClientId;
        DisplayName = client.DisplayName;
        Type = client.Type;
        Resource = client.Resource;
        RedirectUris = string.Join("\n", client.RedirectUris);
        PostLogoutRedirectUris = string.Join("\n", client.PostLogoutRedirectUris);
        SelectedScopes = client.Scopes.ToArray();
        HonorReauthenticationPrompts = client.HonorReauthenticationPrompts;
        ExpectedRevision = client.Revision;
        return page;
    }

    public async Task<IActionResult> OnPostBeginAsync(CancellationToken ct)
    {
        ApplySensitiveResponseHeaders();
        var token = await AccessAsync();
        if (token is null)
        {
            return Challenge(HelloUiDefaults.AuthenticationScheme);
        }
        if (!await IsAuthorizedAsync(options.DeletePolicyName)
            || !await IsAuthorizedAsync(options.ReadPolicyName))
        {
            return Forbid();
        }
        var actor = ActorId();
        if (actor is null)
        {
            return Challenge(HelloUiDefaults.AuthenticationScheme);
        }
        if (!ModelState.IsValid)
        {
            return await LoadAsync(ct);
        }
        if (SelectedScopes.Any(scope => !Catalog.Scopes.Contains(scope, StringComparer.Ordinal)))
        {
            ModelState.AddModelError(string.Empty, text["Admin.Clients.InvalidScopes"]);
            return await LoadAsync(ct);
        }
        var result = await app.BeginAsync(
            token, Action(), context.CreateClientKey(HttpContext), ct);
        if (result.IsSuccess)
        {
            prgState.Remove(ClientsPendingStateToken);
            ClientsPendingStateToken = prgState.Store(new PendingClientAction(
                actor, ActionName, ClientId, DisplayName, Type, Resource,
                RedirectUris, PostLogoutRedirectUris, SelectedScopes,
                HonorReauthenticationPrompts, ExpectedRevision, result.Value.ChallengeId));
            return RedirectToPage("/SkopkaHelloAdmin/Clients", new { Offset });
        }
        else
        {
            AddErrors(result.Errors);
        }
        return await LoadAsync(ct);
    }

    public async Task<IActionResult> OnPostCompleteAsync(Guid challengeId, CancellationToken ct)
    {
        ApplySensitiveResponseHeaders();
        var token = await AccessAsync();
        if (token is null)
        {
            return Challenge(HelloUiDefaults.AuthenticationScheme);
        }
        if (!await IsAuthorizedAsync(options.DeletePolicyName)
            || !await IsAuthorizedAsync(options.ReadPolicyName))
        {
            return Forbid();
        }
        var actor = ActorId();
        if (actor is null)
        {
            return Challenge(HelloUiDefaults.AuthenticationScheme);
        }
        if (!ModelState.IsValid)
        {
            if (challengeId != Guid.Empty)
            {
                ChallengeId = challengeId;
                TempData.Keep(nameof(ClientsPendingStateToken));
            }
            return await LoadAsync(ct);
        }
        var result = await app.CompleteAsync(
            token,
            new HelloAdminClientCompletion(
                Action(), challengeId, VerificationCode ?? string.Empty),
            context.CreateClientKey(HttpContext),
            ct);
        if (result.IsSuccess)
        {
            prgState.Remove(ClientsPendingStateToken);
            ClientsPendingStateToken = null;
            prgState.Remove(ClientsSecretStateToken);
            ClientsSecretStateToken = null;
            if (result.Value.Secret is { } secret)
            {
                ClientsSecretStateToken = prgState.Store(new ClientSecret(actor, secret));
            }
            ClientsStatusMessageTempData = text["Admin.Clients.Completed"];
            return RedirectToPage("/SkopkaHelloAdmin/Clients", new { Offset });
        }
        else
        {
            if (result.Errors.Count == 1
                && result.Errors.First().Code ==
                    Skopka.Identity.Errors.IdentityErrorCodes.VerificationResponseInvalid)
            {
                ChallengeId = challengeId;
                TempData.Keep(nameof(ClientsPendingStateToken));
            }
            else
            {
                prgState.Remove(ClientsPendingStateToken);
                ClientsPendingStateToken = null;
                ResetForm();
            }
            AddErrors(result.Errors);
        }
        return await LoadAsync(ct);
    }

    private async Task<IActionResult> LoadAsync(CancellationToken ct)
    {
        ApplySensitiveResponseHeaders();
        if (!await IsAuthorizedAsync(options.ReadPolicyName))
        {
            return Forbid();
        }
        CanManage = await IsAuthorizedAsync(options.DeletePolicyName);
        var token = await AccessAsync();
        if (token is null)
        {
            return Challenge(HelloUiDefaults.AuthenticationScheme);
        }
        var result = await app.ListAsync(token, Math.Max(0, Offset), 50, ct);
        if (result.IsSuccess)
        {
            Clients = result.Value;
        }
        else
        {
            AddErrors(result.Errors);
        }
        return Page();
    }

    private string? ActorId()
    {
        var subject = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        var session = User.FindFirstValue("sid");
        return string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(session)
            ? null : $"{subject}:{session}";
    }

    private void RestorePending()
    {
        if (!CanManage || !prgState.TryGet<PendingClientAction>(ClientsPendingStateToken, out var pending)
            || pending is null || ActorId() is not { } actor || pending.Actor != actor)
        {
            return;
        }
        ActionName = pending.ActionName;
        ClientId = pending.ClientId;
        DisplayName = pending.DisplayName;
        Type = pending.Type;
        Resource = pending.Resource;
        RedirectUris = pending.RedirectUris;
        PostLogoutRedirectUris = pending.PostLogoutRedirectUris;
        SelectedScopes = pending.SelectedScopes;
        HonorReauthenticationPrompts = pending.HonorReauthenticationPrompts;
        ExpectedRevision = pending.ExpectedRevision;
        ChallengeId = pending.ChallengeId;
        TempData.Keep(nameof(ClientsPendingStateToken));
    }

    private void RestoreSecret()
    {
        if (!CanManage || ActorId() is not { } actor
            || !prgState.TryGet<ClientSecret>(ClientsSecretStateToken, out var stored)
            || stored is null || stored.Actor != actor)
        {
            return;
        }
        if (prgState.TryTake<ClientSecret>(ClientsSecretStateToken, out var secret))
        {
            Secret = secret?.Value;
        }
    }

    private sealed record PendingClientAction(
        string Actor, string ActionName, string? ClientId, string? DisplayName,
        HelloAuthorizationClientType Type, string? Resource, string? RedirectUris,
        string? PostLogoutRedirectUris, string[] SelectedScopes,
        bool HonorReauthenticationPrompts, long? ExpectedRevision, Guid ChallengeId);

    private sealed class ClientSecret(string actor, string value)
    {
        public string Actor { get; } = actor;
        public string Value { get; } = value;
    }

    private void ResetForm()
    {
        ModelState.Clear();
        ActionName = "create";
        ClientId = null;
        DisplayName = null;
        Type = HelloAuthorizationClientType.Public;
        Resource = null;
        RedirectUris = null;
        PostLogoutRedirectUris = null;
        SelectedScopes = [];
        HonorReauthenticationPrompts = true;
        ExpectedRevision = null;
        VerificationCode = null;
        Edit = null;
    }

    private async Task<bool> IsAuthorizedAsync(string policy)
        => (await authorization.AuthorizeAsync(User, HttpContext, policy)).Succeeded;

    private void AddErrors(IReadOnlyCollection<Skopka.Abstraction.OperationResult.Error> errors)
    {
        foreach (var error in errors)
        {
            ModelState.AddModelError(string.Empty, error.Message);
        }
    }

    private void ApplySensitiveResponseHeaders()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }

    private async Task<string?> AccessAsync()
    {
        var authenticated = await HttpContext.AuthenticateAsync(
            HelloUiDefaults.AuthenticationScheme);
        return authenticated.Succeeded
            ? authenticated.Properties?.GetTokenValue(HelloUiDefaults.AccessTokenName)
            : null;
    }
}
