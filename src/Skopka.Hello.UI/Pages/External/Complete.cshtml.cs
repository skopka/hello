using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skopka.Hello.Oidc;

namespace Skopka.Hello.UI.Pages;

public sealed class ExternalCompleteModel(
    IHelloUiExternalApplication application,
    IHelloSessionCookieManager sessionCookies,
    IHelloUiLocalizer text)
    : PageModel
{
    public void OnGet()
        => HelloUiSensitivePage.ApplyResponseHeaders(Response);

    public async Task<IActionResult> OnPostAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        var transport = sessionCookies.ValidateTransport(HttpContext);
        if (!transport.IsSuccess)
        {
            HelloUiModelState.AddErrors(
                ModelState,
                transport.Errors,
                text);
            return Page();
        }

        var result = await application.CompleteChallengeAsync(
            HttpContext,
            cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(
                ModelState,
                result.Errors,
                text);
            return Page();
        }

        return result.Value.Kind switch
        {
            HelloOidcCompletionKind.SignedIn =>
                await CompleteSignInAsync(result.Value),
            HelloOidcCompletionKind.RegistrationRequired =>
                RedirectToPage("/SkopkaHello/External/Register"),
            HelloOidcCompletionKind.LinkPending =>
                RedirectToPage(
                    "/SkopkaHello/Account/ExternalLogins",
                    new { pending = true }),
            _ => InvalidCompletion(),
        };
    }

    public async Task<IActionResult> OnPostCancelAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        await application.ClearBrowserFlowAsync(
            HttpContext,
            cancellationToken);
        return RedirectToPage("/SkopkaHello/Login");
    }

    private async Task<IActionResult> CompleteSignInAsync(
        HelloUiExternalCompletion completion)
    {
        if (completion.SignIn is null)
        {
            return InvalidCompletion();
        }

        await HelloUiSession.EstablishAsync(
            HttpContext,
            sessionCookies,
            completion.SignIn);
        return Url.IsLocalUrl(completion.ReturnUrl)
            ? LocalRedirect(completion.ReturnUrl)
            : RedirectToPage("/SkopkaHello/Account/Index");
    }

    private PageResult InvalidCompletion()
    {
        ModelState.AddModelError(
            string.Empty,
            text["ExternalComplete.Invalid"]);
        return Page();
    }
}
