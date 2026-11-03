using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skopka.Hello.Oidc;

namespace Skopka.Hello.UI.Pages;

public sealed class ExternalCompleteModel(
    IHelloUiExternalApplication application,
    IHelloSessionCookieManager sessionCookies,
    IHelloUiLocalizer text,
    IHelloUiApplication? identity = null,
    HelloUiPrgStateStore? prgState = null)
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
            HelloOidcCompletionKind.TotpAuthorized =>
                await CompleteTotpAsync(result.Value, cancellationToken),
            _ => InvalidCompletion(),
        };
    }

    private async Task<IActionResult> CompleteTotpAsync(
        HelloUiExternalCompletion completion, CancellationToken cancellationToken)
    {
        if (identity is null || prgState is null
            || string.IsNullOrEmpty(completion.TotpProof))
        {
            return InvalidCompletion();
        }
        var result = await identity.BeginTotpEnrollmentAsync(
            HttpContext, cancellationToken, proof: completion.TotpProof);
        if (!result.IsSuccess)
        {
            return RedirectToPage("/SkopkaHello/Account/Security",
                new { totpExternalError = true });
        }
        TempData["EnrollmentStateToken"] = prgState.Store(result.Value);
        return RedirectToPage("/SkopkaHello/Account/Security");
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
