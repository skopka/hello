using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skopka.Identity;

namespace Skopka.Hello.UI.Pages;

public sealed class ConfirmPhoneModel(
    IHelloUiApplication application,
    IIdentityNormalizer normalizer,
    IHelloUiLocalizer text)
    : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid UserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Phone { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string Token { get; set; } = string.Empty;

    public bool LinkValid { get; private set; }

    [BindProperty(SupportsGet = true)]
    public bool Confirmed { get; set; }

    public void OnGet()
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (Confirmed)
        {
            return;
        }

        LinkValid = IsLinkValid();
    }

    public async Task<IActionResult> OnPostAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        LinkValid = IsLinkValid();
        if (!LinkValid)
        {
            ModelState.AddModelError(
                string.Empty,
                text["ConfirmPhone.InvalidLink"]);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await application.ConfirmPhoneAsync(
            new HelloUiConfirmPhoneCommand(
                UserId,
                Phone,
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

        return RedirectToPage(
            "/SkopkaHello/ConfirmPhone",
            new { confirmed = true });
    }

    private bool IsLinkValid()
        => UserId != Guid.Empty
            && !string.IsNullOrWhiteSpace(Token)
            && normalizer.NormalizePhoneLoginIdentifier(Phone)
                is not null;
}
