using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skopka.Identity.Authentication;

namespace Skopka.Hello.UI.Pages;

public sealed class ResendPhoneConfirmationModel(
    IHelloUiApplication application)
    : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool Sent { get; private set; }

    public void OnGet()
        => HelloUiSensitivePage.ApplyResponseHeaders(Response);

    public async Task<IActionResult> OnPostAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await application.RequestPhoneConfirmationAsync(
            Input.Phone,
            HttpContext,
            cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, result.Errors);
            return Page();
        }

        Sent = true;
        return Page();
    }

    public sealed class InputModel
    {
        [Required]
        [StringLength(IdentityLoginLimits.MaximumLoginLength)]
        public string Phone { get; set; } = string.Empty;
    }
}
