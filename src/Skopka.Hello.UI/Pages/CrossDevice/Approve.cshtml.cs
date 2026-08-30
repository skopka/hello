using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Skopka.Hello.UI.Pages;

[Authorize(Policy = HelloUiDefaults.AuthorizationPolicy)]
public sealed class CrossDeviceApproveModel(
    IHelloUiCrossDeviceApplication application,
    IHelloUiLocalizer text)
    : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string DeviceCode { get; set; } = string.Empty;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public Guid ChallengeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTimeOffset? ChallengeExpiresAt { get; set; }

    public HelloCrossDeviceApprovalDetails? Details { get; private set; }

    [BindProperty(SupportsGet = true)]
    public bool Approved { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Denied { get; set; }

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (Approved || Denied)
        {
            return Page();
        }

        await LoadDetailsAsync(cancellationToken);
        if (ChallengeId != Guid.Empty)
        {
            Input.ChallengeId = ChallengeId;
            Input.ExpiresAt = ChallengeExpiresAt;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostRequestCodeAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        ModelState.Clear();
        if (!await LoadDetailsAsync(cancellationToken))
        {
            return Page();
        }

        var challenge = await application.BeginApprovalAsync(
            DeviceCode,
            HttpContext,
            cancellationToken);
        if (!challenge.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, challenge.Errors, text);
            return Page();
        }

        return RedirectToPage(
            "/SkopkaHello/CrossDevice/Approve",
            new
            {
                deviceCode = DeviceCode,
                challengeId = challenge.Value.ChallengeId,
                challengeExpiresAt = challenge.Value.ExpiresAt,
            });
    }

    public async Task<IActionResult> OnPostApproveAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (!await LoadDetailsAsync(cancellationToken))
        {
            return Page();
        }

        if (Input.ChallengeId == Guid.Empty)
        {
            ModelState.AddModelError(
                string.Empty,
                text["CrossDevice.Approve.RequestCodeFirst"]);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await application.ApproveAsync(
            DeviceCode,
            Input.ChallengeId,
            Input.TotpCode,
            HttpContext,
            cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, result.Errors, text);
            return Page();
        }

        return RedirectToPage(
            "/SkopkaHello/CrossDevice/Approve",
            new { deviceCode = DeviceCode, approved = true });
    }

    public async Task<IActionResult> OnPostDenyAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        ModelState.Clear();
        if (!await LoadDetailsAsync(cancellationToken))
        {
            return Page();
        }

        var result = await application.DenyAsync(
            DeviceCode,
            HttpContext,
            cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, result.Errors, text);
            return Page();
        }

        return RedirectToPage(
            "/SkopkaHello/CrossDevice/Approve",
            new { deviceCode = DeviceCode, denied = true });
    }

    private async Task<bool> LoadDetailsAsync(
        CancellationToken cancellationToken)
    {
        var result = await application.GetApprovalDetailsAsync(
            DeviceCode,
            HttpContext,
            cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, result.Errors, text);
            return false;
        }

        Details = result.Value;
        return true;
    }

    public sealed class InputModel
    {
        public Guid ChallengeId { get; set; }

        public DateTimeOffset? ExpiresAt { get; set; }

        [Required(ErrorMessage = "Validation.Required")]
        [StringLength(
            64,
            MinimumLength = 6,
            ErrorMessage = "Validation.StringLengthRange")]
        [Display(Name = "Field.VerificationCode")]
        public string TotpCode { get; set; } = string.Empty;
    }
}
