using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Skopka.Hello.UI.Pages;

public sealed class ResendConfirmationModel(
    IHelloUiApplication application,
    SkopkaHelloUiOptions uiOptions,
    IHelloUiLocalizer text)
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

        if (!uiOptions.ContactConfirmation
                .IsEmailConfirmationRequired(Input.Email))
        {
            Sent = true;
            return Page();
        }

        var result = await application.RequestEmailConfirmationAsync(
            Input.Email,
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

        Sent = true;
        return Page();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Validation.Required")]
        [EmailAddress(ErrorMessage = "Validation.EmailAddress")]
        [StringLength(320, ErrorMessage = "Validation.StringLength")]
        public string Email { get; set; } = string.Empty;
    }
}
