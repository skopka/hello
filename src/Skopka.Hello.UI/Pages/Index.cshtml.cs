using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Skopka.Hello.UI.Pages;

public sealed class IndexModel : PageModel
{
    public async Task<IActionResult> OnGetAsync()
    {
        var authentication = await HttpContext.AuthenticateAsync(
            HelloUiDefaults.AuthenticationScheme);
        return Redirect(
            authentication.Succeeded
                ? HelloUiDefaults.AccountPath
                : HelloUiDefaults.LoginPath);
    }
}
