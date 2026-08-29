using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skopka.Identity.DeviceAuthorization;

namespace Skopka.Hello.UI.Pages;

public sealed class CrossDeviceWaitingModel(
    IHelloUiCrossDeviceApplication application,
    IHelloSessionCookieManager sessionCookies,
    SkopkaHelloUiOptions uiOptions,
    HelloUiRoutePaths routes,
    IHelloUiLocalizer text)
    : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string DeviceCode { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public HelloUiCrossDeviceWaiting? RequestState { get; private set; }

    public int PollingIntervalMilliseconds =>
        checked((int)application.PollingInterval.TotalMilliseconds);

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnGetStatusAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        var status = await application.GetWaitingAsync(
            DeviceCode,
            HttpContext,
            cancellationToken);
        if (!status.IsSuccess)
        {
            return new JsonResult(new { state = "invalid" })
            {
                StatusCode = StatusCodes.Status400BadRequest,
            };
        }

        return new JsonResult(new
        {
            state = status.Value.State.ToString().ToLowerInvariant(),
            expiresAt = status.Value.ExpiresAt,
        });
    }

    public async Task<IActionResult> OnPostCompleteAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        ModelState.Clear();
        var transport = sessionCookies.ValidateTransport(HttpContext);
        if (!transport.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, transport.Errors, text);
            await LoadAsync(cancellationToken);
            return Page();
        }

        var completed = await application.CompleteAsync(
            DeviceCode,
            HttpContext,
            cancellationToken);
        if (!completed.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, completed.Errors, text);
            await LoadAsync(cancellationToken);
            return Page();
        }

        await HelloUiSession.EstablishAsync(
            HttpContext,
            sessionCookies,
            completed.Value.SignIn);
        return Url.IsLocalUrl(completed.Value.ReturnUrl)
            ? LocalRedirect(completed.Value.ReturnUrl)
            : LocalRedirect(uiOptions.GetAuthenticatedRedirectPath(routes));
    }

    public async Task<IActionResult> OnPostRestartAsync(
        CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        ModelState.Clear();
        var transport = sessionCookies.ValidateTransport(HttpContext);
        if (!transport.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, transport.Errors, text);
            await LoadAsync(cancellationToken);
            return Page();
        }

        var result = await application.BeginAsync(
            ReturnUrl,
            HttpContext,
            cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, result.Errors, text);
            return Page();
        }

        return RedirectToPage(
            "/SkopkaHello/CrossDevice/Waiting",
            new
            {
                deviceCode = result.Value.DeviceCode,
                returnUrl = Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : null,
            });
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(DeviceCode))
        {
            ModelState.AddModelError(
                string.Empty,
                text["CrossDevice.Invalid"]);
            return false;
        }

        var result = await application.GetWaitingAsync(
            DeviceCode,
            HttpContext,
            cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, result.Errors, text);
            return false;
        }

        RequestState = result.Value;
        return true;
    }
}
