using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Skopka.Hello.UI.Pages;

[Authorize(Policy = HelloUiDefaults.AuthorizationPolicy)]
public sealed class CrossDeviceRequestsModel(
    IHelloUiCrossDeviceApplication application,
    IHelloUiLocalizer text)
    : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IActionResult OnGet()
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await application.GetApprovalDetailsByUserCodeAsync(
            Input.UserCode,
            HttpContext,
            cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, result.Errors, text);
            return Page();
        }

        return RedirectToPage(
            "/SkopkaHello/CrossDevice/Approve",
            new { deviceCode = result.Value.DeviceCode });
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Validation.Required")]
        [StringLength(
            32,
            MinimumLength = 4,
            ErrorMessage = "Validation.StringLengthRange")]
        [Display(Name = "CrossDevice.Code")]
        public string UserCode { get; set; } = string.Empty;
    }
}
