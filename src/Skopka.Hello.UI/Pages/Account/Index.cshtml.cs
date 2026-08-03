using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Skopka.Hello.UI.Pages;

[Authorize(Policy = HelloUiDefaults.AuthorizationPolicy)]
public sealed class AccountModel(
    IHelloUiApplication application,
    IHelloSessionCookieManager sessionCookies,
    IHelloAccountMessageSender messageSender)
    : PageModel
{
    public Guid UserId { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public string? UserName { get; private set; }

    public string? Email { get; private set; }

    public bool EmailConfirmed { get; private set; }

    public string? Phone { get; private set; }

    public bool PhoneConfirmed { get; private set; }

    public bool EmailConfirmationRequested { get; private set; }

    public bool PhoneConfirmationRequested { get; private set; }

    public IActionResult OnGet()
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        return LoadAccount();
    }

    public async Task<IActionResult>
        OnPostRequestEmailConfirmationAsync(
            CancellationToken cancellationToken)
    {
        var loaded = LoadAccount();
        if (loaded is not PageResult)
        {
            return loaded;
        }

        if (string.IsNullOrWhiteSpace(Email))
        {
            ModelState.AddModelError(
                string.Empty,
                "The account does not have an email address.");
            return Page();
        }

        var deliveryAvailable = messageSender.CheckAvailability(
            HelloDeliveryChannel.Email);
        if (!deliveryAvailable.IsSuccess)
        {
            HelloUiModelState.AddErrors(
                ModelState,
                deliveryAvailable.Errors);
            return Page();
        }

        var result =
            await application.RequestEmailConfirmationAsync(
                Email,
                HttpContext,
                cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(
                ModelState,
                result.Errors);
            return Page();
        }

        EmailConfirmationRequested = true;
        return Page();
    }

    public async Task<IActionResult>
        OnPostRequestPhoneConfirmationAsync(
            CancellationToken cancellationToken)
    {
        var loaded = LoadAccount();
        if (loaded is not PageResult)
        {
            return loaded;
        }

        if (string.IsNullOrWhiteSpace(Phone))
        {
            ModelState.AddModelError(
                string.Empty,
                "The account does not have a phone number.");
            return Page();
        }

        var deliveryAvailable = messageSender.CheckAvailability(
            HelloDeliveryChannel.Sms);
        if (!deliveryAvailable.IsSuccess)
        {
            HelloUiModelState.AddErrors(
                ModelState,
                deliveryAvailable.Errors);
            return Page();
        }

        var result =
            await application.RequestPhoneConfirmationAsync(
                Phone,
                HttpContext,
                cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(
                ModelState,
                result.Errors);
            return Page();
        }

        PhoneConfirmationRequested = true;
        return Page();
    }

    private IActionResult LoadAccount()
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (!HelloUiPrincipalFactory.TryGetUserId(
                User,
                out var userId))
        {
            return Challenge();
        }

        UserId = userId;
        DisplayName = User.FindFirstValue(
                HelloUiPrincipalFactory.DisplayNameClaim)
            ?? User.Identity?.Name
            ?? "Account";
        UserName = User.Identity?.Name;
        Email = User.FindFirstValue(ClaimTypes.Email);
        EmailConfirmed = bool.TryParse(
            User.FindFirstValue(
                HelloUiPrincipalFactory.EmailConfirmedClaim),
            out var confirmed)
            && confirmed;
        Phone = User.FindFirstValue(ClaimTypes.MobilePhone);
        PhoneConfirmed = bool.TryParse(
            User.FindFirstValue(
                HelloUiPrincipalFactory.PhoneConfirmedClaim),
            out var phoneConfirmed)
            && phoneConfirmed;
        return Page();
    }

    public async Task<IActionResult> OnPostLogoutAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        var refreshToken = sessionCookies.ReadRefreshToken(
            HttpContext);
        if (refreshToken is not null)
        {
            await application.LogoutAsync(
                refreshToken,
                cancellationToken);
        }

        sessionCookies.DeleteSessionCookies(HttpContext);
        await HttpContext.SignOutAsync(
            HelloUiDefaults.AuthenticationScheme);
        return RedirectToPage("/SkopkaHello/Login");
    }
}
