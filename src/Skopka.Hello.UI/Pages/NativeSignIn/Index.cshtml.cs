using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace Skopka.Hello.UI.Pages;

public sealed class NativeSignInModel : PageModel
{
    private readonly IHelloUiNativeSignInApplication application;
    private readonly IHelloSessionCookieManager cookies;
    private readonly IDataProtector protector;
    private readonly SkopkaHelloUiOptions options;
    private readonly HelloUiRoutePaths routes;

    public NativeSignInModel(IServiceProvider services)
    {
        application = services.GetRequiredService<IHelloUiNativeSignInApplication>();
        cookies = services.GetRequiredService<IHelloSessionCookieManager>();
        protector = services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Skopka.Hello.NativeSignIn.WebView.v1");
        options = services.GetRequiredService<SkopkaHelloUiOptions>();
        routes = services.GetRequiredService<HelloUiRoutePaths>();
    }

    private IHelloUiLocalizer Text => HttpContext.RequestServices.GetRequiredService<IHelloUiLocalizer>();

    private string Cookie => "__Host-Skopka.Hello.Native.WebView."
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Request.PathBase + routes.PathPrefix)))[..16];

    public IActionResult OnGet()
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        Response.Headers["Referrer-Policy"] = "same-origin";
        return Page();
    }

    public async Task<IActionResult> OnPostStartAsync(
        string clientId, string callback, string state, string codeChallenge,
        string? returnUrl, CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (!OriginValid()
            || (await HttpContext.AuthenticateAsync(HelloUiDefaults.AuthenticationScheme)).Succeeded
            || !cookies.ValidateTransport(HttpContext).IsSuccess
            || (returnUrl is not null && !Url.IsLocalUrl(returnUrl)))
        {
            return BadRequest();
        }

        var result = await application.BeginAsync(
            clientId, callback, state, codeChallenge, returnUrl, HttpContext, cancellationToken);
        if (!result.IsSuccess)
        {
            return HelloUiNativeFailure.Result(HttpContext, result.Errors, Text);
        }

        var value = result.Value;
        var binding = protector.Protect(JsonSerializer.Serialize(new Binding(value.FlowId, value.WebViewBinder)));
        Response.Cookies.Append(Cookie, binding, CookieOptions(value.ExpiresAt));
        var browser = Url.Page("/SkopkaHello/NativeSignIn/Browser")!;
        return new JsonResult(new
        {
            launchUrl = browser + "#launch=" + Uri.EscapeDataString(value.FlowId + "." + value.LaunchSecret),
            expiresAt = value.ExpiresAt,
        });
    }

    public async Task<IActionResult> OnPostStatusAsync(CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (!OriginValid() || ReadBinding() is not { } binding)
        {
            return BadRequest();
        }

        var result = await application.StatusAsync(binding.FlowId, binding.Binder, HttpContext, cancellationToken);
        return result.IsSuccess
            ? new JsonResult(new { state = result.Value.State.ToString().ToLowerInvariant() })
            : HelloUiNativeFailure.Result(HttpContext, result.Errors, Text);
    }

    public async Task<IActionResult> OnPostCancelAsync(CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (!OriginValid() || ReadBinding() is not { } binding)
        {
            return BadRequest();
        }

        var result = await application.CancelAsync(binding.FlowId, binding.Binder, HttpContext, cancellationToken);
        return result.IsSuccess
            ? new JsonResult(new { state = "cancelled" })
            : HelloUiNativeFailure.Result(HttpContext, result.Errors, Text);
    }

    public async Task<IActionResult> OnPostCompleteAsync(
        string codeVerifier, string completionReceipt, CancellationToken cancellationToken)
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        if (!OriginValid()
            || (await HttpContext.AuthenticateAsync(HelloUiDefaults.AuthenticationScheme)).Succeeded
            || !cookies.ValidateTransport(HttpContext).IsSuccess
            || ReadBinding() is not { } binding)
        {
            return BadRequest();
        }

        var result = await application.CompleteAsync(
            binding.FlowId, binding.Binder, codeVerifier, completionReceipt, HttpContext, cancellationToken);
        if (!result.IsSuccess)
        {
            return HelloUiNativeFailure.Result(HttpContext, result.Errors, Text);
        }

        await HelloUiSession.EstablishAsync(HttpContext, cookies, result.Value.SignIn);
        Response.Cookies.Delete(Cookie, CookieOptions());
        return Url.IsLocalUrl(result.Value.ReturnUrl)
            ? LocalRedirect(result.Value.ReturnUrl!)
            : LocalRedirect(Request.PathBase + HelloUiSite.Redirect(HttpContext, options, routes));
    }

    private bool OriginValid()
    {
        var configured = HttpContext.RequestServices.GetRequiredService<IHelloSiteContext>().Current?.PublicOrigin;
        return configured is not null && Request.IsHttps && Request.Headers.Origin.Count == 1
            && Uri.TryCreate(Request.Headers.Origin.ToString(), UriKind.Absolute, out var origin)
            && origin.AbsolutePath == "/" && origin.Query.Length == 0
            && origin.Fragment.Length == 0 && origin.UserInfo.Length == 0
            && origin.Scheme == configured.Scheme
            && origin.Authority.Equals(configured.Authority, StringComparison.OrdinalIgnoreCase);
    }

    private static CookieOptions CookieOptions(DateTimeOffset? expires = null) => new()
    {
        Secure = true,
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        IsEssential = true,
        Path = "/",
        Expires = expires,
    };

    private Binding? ReadBinding()
    {
        try
        {
            return Request.Cookies.TryGetValue(Cookie, out var value)
                ? JsonSerializer.Deserialize<Binding>(protector.Unprotect(value))
                : null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return null;
        }
    }

    private sealed record Binding(string FlowId, string Binder);
}
