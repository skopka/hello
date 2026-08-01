using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skopka.Hello.Oidc;

namespace Skopka.Hello.UI.Pages;

public sealed class ExternalCompleteModel(
    IHelloUiExternalApplication application,
    IHelloSessionCookieManager sessionCookies)
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
            HelloUiModelState.AddErrors(ModelState, transport.Errors);
            return Page();
        }

        var result = await application.CompleteChallengeAsync(
            HttpContext,
            cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, result.Errors);
            return Page();
        }

        return result.Value.Kind switch
        {
            HelloOidcCompletionKind.SignedIn =>
                await CompleteSignInAsync(result.Value),
            HelloOidcCompletionKind.RegistrationRequired =>
                Redirect(HelloUiDefaults.ExternalRegistrationPath),
            HelloOidcCompletionKind.LinkPending =>
                Redirect($"{HelloUiDefaults.ExternalLoginsPath}?pending=true"),
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
        return Redirect(HelloUiDefaults.LoginPath);
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
            : Redirect(HelloUiDefaults.AccountPath);
    }

    private PageResult InvalidCompletion()
    {
        ModelState.AddModelError(
            string.Empty,
            "The external sign-in attempt is invalid or expired.");
        return Page();
    }
}
