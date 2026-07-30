using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Skopka.Hello.UI.Pages;

[Authorize(Policy = HelloUiDefaults.AuthorizationPolicy)]
public sealed class AccountModel(
    IHelloUiApplication application,
    IHelloSessionCookieManager sessionCookies)
    : PageModel
{
    public Guid UserId { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public string? UserName { get; private set; }

    public string? Email { get; private set; }

    public bool EmailConfirmed { get; private set; }

    public IActionResult OnGet()
    {
        if (!HelloUiPrincipalFactory.TryGetUserId(
                User,
                out var userId))
        {
            return Challenge();
        }

        UserId = userId;
        DisplayName = User.FindFirstValue(
                HelloUiPrincipalFactory.DisplayNameClaim)
            ?? User.Identity?.Name
            ?? "Account";
        UserName = User.Identity?.Name;
        Email = User.FindFirstValue(ClaimTypes.Email);
        EmailConfirmed = bool.TryParse(
            User.FindFirstValue(
                HelloUiPrincipalFactory.EmailConfirmedClaim),
            out var confirmed)
            && confirmed;
        return Page();
    }

    public async Task<IActionResult> OnPostLogoutAsync(
        CancellationToken cancellationToken)
    {
        var refreshToken = sessionCookies.ReadRefreshToken(
            HttpContext);
        if (refreshToken is not null)
        {
            await application.LogoutAsync(
                refreshToken,
                cancellationToken);
        }

        sessionCookies.DeleteSessionCookies(HttpContext);
        await HttpContext.SignOutAsync(
            HelloUiDefaults.AuthenticationScheme);
        return Redirect(HelloUiDefaults.LoginPath);
    }
}
