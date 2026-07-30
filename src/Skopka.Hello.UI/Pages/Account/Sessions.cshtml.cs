using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Skopka.Hello.UI.Pages;

[Authorize(Policy = HelloUiDefaults.AuthorizationPolicy)]
public sealed class SessionsModel(
    IHelloUiApplication application,
    IHelloSessionCookieManager sessionCookies)
    : PageModel
{
    public IReadOnlyList<HelloSessionInfo> Sessions { get; private set; } =
        Array.Empty<HelloSessionInfo>();

    public Guid CurrentSessionId { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var userId, out _))
        {
            return Challenge();
        }

        await LoadAsync(userId, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostRevokeAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(
                out var userId,
                out var currentSessionId))
        {
            return Challenge();
        }

        var result = await application.RevokeSessionAsync(
            userId,
            sessionId,
            cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(
                ModelState,
                result.Errors);
            await LoadAsync(userId, cancellationToken);
            return Page();
        }

        if (sessionId == currentSessionId)
        {
            await ClearLocalSessionAsync();
            return Redirect(HelloUiDefaults.LoginPath);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostLogoutAllAsync(
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var userId, out _))
        {
            return Challenge();
        }

        var result = await application.LogoutAllAsync(
            userId,
            cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(
                ModelState,
                result.Errors);
            await LoadAsync(userId, cancellationToken);
            return Page();
        }

        await ClearLocalSessionAsync();
        return Redirect(HelloUiDefaults.LoginPath);
    }

    private bool TryGetIdentity(
        out Guid userId,
        out Guid sessionId)
    {
        sessionId = default;
        return HelloUiPrincipalFactory.TryGetUserId(
                User,
                out userId)
            && HelloUiPrincipalFactory.TryGetSessionId(
                User,
                out sessionId);
    }

    private async Task LoadAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        HelloUiPrincipalFactory.TryGetSessionId(
            User,
            out var currentSessionId);
        CurrentSessionId = currentSessionId;

        var result = await application.ListSessionsAsync(
            userId,
            cancellationToken);
        if (result.IsSuccess)
        {
            Sessions = result.Value;
            return;
        }

        HelloUiModelState.AddErrors(
            ModelState,
            result.Errors);
    }

    private async Task ClearLocalSessionAsync()
    {
        sessionCookies.DeleteSessionCookies(HttpContext);
        await HttpContext.SignOutAsync(
            HelloUiDefaults.AuthenticationScheme);
    }
}
