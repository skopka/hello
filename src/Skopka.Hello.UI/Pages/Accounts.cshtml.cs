using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Skopka.Hello.UI.Pages;

[AllowAnonymous]
public sealed class AccountsModel(
    IHelloUiAccountSwitcher accountSwitcher,
    IHelloSessionCookieManager sessionCookies,
    SkopkaHelloUiOptions uiOptions,
    HelloUiRoutePaths routes,
    IHelloUiLocalizer text)
    : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IReadOnlyList<HelloUiSavedAccount> Accounts { get; private set; } =
        [];

    public IActionResult OnGet()
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (!uiOptions.AccountSwitching.Enabled)
        {
            return NotFound();
        }

        NormalizeReturnUrl();
        LoadAccounts();
        return Page();
    }

    public async Task<IActionResult> OnPostSwitchAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (!uiOptions.AccountSwitching.Enabled)
        {
            return NotFound();
        }

        NormalizeReturnUrl();
        var result = await accountSwitcher.SwitchAsync(
            HttpContext,
            userId,
            cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, result.Errors, text);
            LoadAccounts();
            return Page();
        }

        await HelloUiSession.EstablishAsync(
            HttpContext,
            sessionCookies,
            result.Value);
        return Continue();
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (!uiOptions.AccountSwitching.Enabled)
        {
            return NotFound();
        }

        NormalizeReturnUrl();
        sessionCookies.DeleteSessionCookies(HttpContext);
        await HttpContext.SignOutAsync(
            HelloUiDefaults.AuthenticationScheme);
        return RedirectToPage(
            "/SkopkaHello/Login",
            new { returnUrl = ReturnUrl });
    }

    public async Task<IActionResult> OnPostRemoveAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (!uiOptions.AccountSwitching.Enabled)
        {
            return NotFound();
        }

        NormalizeReturnUrl();
        var accounts = accountSwitcher.List(HttpContext);
        var removingCurrent = accounts.Any(account =>
            account.UserId == userId && account.IsCurrent);
        await accountSwitcher.RemoveAsync(
            HttpContext,
            userId,
            revokeSession: true,
            cancellationToken);
        if (removingCurrent)
        {
            sessionCookies.DeleteSessionCookies(HttpContext);
            await HttpContext.SignOutAsync(
                HelloUiDefaults.AuthenticationScheme);
        }

        var remaining = accounts.Count(account => account.UserId != userId);
        return remaining > 0
            ? RedirectToPage(
                "/SkopkaHello/Accounts",
                new { returnUrl = ReturnUrl })
            : RedirectToPage(
                "/SkopkaHello/Login",
                new { returnUrl = ReturnUrl });
    }

    private LocalRedirectResult Continue()
        => Url.IsLocalUrl(ReturnUrl)
            ? LocalRedirect(ReturnUrl!)
            : LocalRedirect(
                uiOptions.GetAuthenticatedRedirectPath(routes));

    private void NormalizeReturnUrl()
    {
        if (!Url.IsLocalUrl(ReturnUrl))
        {
            ReturnUrl = null;
        }
    }

    private void LoadAccounts()
        => Accounts = accountSwitcher.List(HttpContext);
}
