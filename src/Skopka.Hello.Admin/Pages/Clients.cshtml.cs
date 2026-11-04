using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skopka.Hello.AuthorizationServer;
using Skopka.Hello.UI;

namespace Skopka.Hello.Admin.Pages;

[Authorize(Policy = HelloUiDefaults.AuthorizationPolicy)]
public sealed class ClientsModel(
    IHelloAdminClientApplication app,
    IHelloAuthorizationClientManagement clients,
    IHelloRequestContext context,
    IAuthorizationService authorization,
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
        if (page is not PageResult || string.IsNullOrWhiteSpace(Edit))
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
            ChallengeId = result.Value.ChallengeId;
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
        if (!ModelState.IsValid)
        {
            if (challengeId != Guid.Empty)
            {
                ChallengeId = challengeId;
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
            Secret = result.Value.Secret;
            StatusMessage = text["Admin.Clients.Completed"];
            ResetForm();
        }
        else
        {
            if (result.Errors.Count == 1
                && result.Errors.First().Code ==
                    Skopka.Identity.Errors.IdentityErrorCodes.VerificationResponseInvalid)
            {
                ChallengeId = challengeId;
            }
            else
            {
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
