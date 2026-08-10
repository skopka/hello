using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Skopka.Hello.UI.Pages;

public sealed class ConfirmEmailModel(
    IHelloUiApplication application,
    IHelloUiLocalizer text)
    : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid UserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Email { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string Token { get; set; } = string.Empty;

    public bool LinkValid { get; private set; }

    public bool Confirmed { get; private set; }

    public bool AutoSubmit { get; private set; }

    public void OnGet()
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        LinkValid = IsLinkValid();
        AutoSubmit = LinkValid;
    }

    public async Task<IActionResult> OnPostAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        LinkValid = IsLinkValid();
        AutoSubmit = false;
        if (!LinkValid)
        {
            ModelState.AddModelError(
                string.Empty,
                text["ConfirmEmail.InvalidLink"]);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await application.ConfirmEmailAsync(
            new HelloUiConfirmEmailCommand(
                UserId,
                Email,
                Token),
            cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(
                ModelState,
                result.Errors,
                text);
            return Page();
        }

        Confirmed = true;
        return Page();
    }

    private bool IsLinkValid()
        => UserId != Guid.Empty
            && !string.IsNullOrWhiteSpace(Token)
            && new EmailAddressAttribute().IsValid(Email);
}
