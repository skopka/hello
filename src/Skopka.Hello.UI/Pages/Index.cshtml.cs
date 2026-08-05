using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Skopka.Hello.UI.Pages;

public sealed class IndexModel(
    SkopkaHelloUiOptions uiOptions,
    HelloUiRoutePaths routes)
    : PageModel
{
    public async Task<IActionResult> OnGetAsync()
    {
        var authentication = await HttpContext.AuthenticateAsync(
            HelloUiDefaults.AuthenticationScheme);
        return authentication.Succeeded
            ? LocalRedirect(
                uiOptions.GetAuthenticatedRedirectPath(routes))
            : RedirectToPage("/SkopkaHello/Login");
    }
}
