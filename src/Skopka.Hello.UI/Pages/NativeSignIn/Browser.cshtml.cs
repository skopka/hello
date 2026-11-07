using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Skopka.Hello.UI.Pages;

public sealed class NativeSignInBrowserModel : PageModel
{
    private readonly IHelloUiNativeSignInApplication application;
    private readonly IHelloUiUserAccessor users;
    private readonly IDataProtector protector;
    private readonly HelloUiRoutePaths routes;
    private IHelloUiLocalizer Text => HttpContext.RequestServices.GetRequiredService<IHelloUiLocalizer>();
    public NativeSignInBrowserModel(IServiceProvider services)
    {
        application = services.GetRequiredService<IHelloUiNativeSignInApplication>();
        users = services.GetRequiredService<IHelloUiUserAccessor>();
        protector = services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Skopka.Hello.NativeSignIn.Browser.v1");
        routes = services.GetRequiredService<HelloUiRoutePaths>();
    }
    private string Cookie => "__Host-Skopka.Hello.Native.Browser." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Request.PathBase + routes.PathPrefix)))[..16];
    public string? ClientName { get; private set; }
    public string? AccountName { get; private set; }
    public string Nonce { get; private set; } = string.Empty;
    public IActionResult OnGet()
    {
        Headers();
        Nonce = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        Response.Headers.ContentSecurityPolicy = $"default-src 'none'; script-src 'nonce-{Nonce}'; connect-src 'self'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";
        return Page();
    }
    public async Task<IActionResult> OnGetConfirmAsync(CancellationToken cancellationToken)
    {
        Headers();
        Response.Headers["Referrer-Policy"] = "same-origin";
        if (ReadBinding() is not { } binding) return BadRequest();
        var details = await application.DetailsAsync(binding.FlowId, binding.Binder, HttpContext, cancellationToken);
        if (!details.IsSuccess) return HelloUiNativeFailure.Result(HttpContext, details.Errors, Text);
        var user = await users.GetAsync(HttpContext, cancellationToken);
        if (user is null) return RedirectToPage("/SkopkaHello/Login", new { returnUrl = Url.Page("/SkopkaHello/NativeSignIn/Browser", "Confirm") });
        ClientName = details.Value.ClientDisplayName; AccountName = user.DisplayName;
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";
        return Page();
    }
    public async Task<IActionResult> OnPostAttachAsync(string launch, CancellationToken cancellationToken)
    {
        Headers();
        if (!OriginValid() || string.IsNullOrWhiteSpace(launch)) return BadRequest();
        var parts = launch.Split('.', 2);
        if (parts.Length != 2) return BadRequest();
        var result = await application.AttachAsync(parts[0], parts[1], HttpContext, cancellationToken);
        if (!result.IsSuccess) return HelloUiNativeFailure.Result(HttpContext, result.Errors, Text);
        Response.Cookies.Append(Cookie, protector.Protect(JsonSerializer.Serialize(new Binding(result.Value.FlowId, result.Value.BrowserBinder))), CookieOptions(result.Value.ExpiresAt));
        return RedirectToPage(null, "Confirm");
    }
    public async Task<IActionResult> OnPostConfirmAsync(bool approve, CancellationToken cancellationToken)
    {
        Headers();
        if (!OriginValid() || ReadBinding() is not { } binding) return BadRequest();
        var user = await users.GetAsync(HttpContext, cancellationToken);
        var auth = await HttpContext.AuthenticateAsync(HelloUiDefaults.AuthenticationScheme);
        var token = auth.Properties?.GetTokenValue(HelloUiDefaults.AccessTokenName);
        if (user is null || string.IsNullOrEmpty(token)) return Unauthorized();
        var result = await application.ConfirmAsync(binding.FlowId, binding.Binder, token, user.SessionId, approve, HttpContext, cancellationToken);
        if (!result.IsSuccess) return HelloUiNativeFailure.Result(HttpContext, result.Errors, Text);
        Response.Cookies.Delete(Cookie, CookieOptions());
        var destination = result.Value.Callback + "#state=" + Uri.EscapeDataString(result.Value.State);
        if (result.Value.CompletionReceipt is { } receipt)
        {
            destination += "&receipt=" + Uri.EscapeDataString(receipt);
        }
        var nonce = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        Response.Headers.ContentSecurityPolicy = $"default-src 'none'; script-src 'nonce-{nonce}'; base-uri 'none'; frame-ancestors 'none'";
        var encoded = JsonSerializer.Serialize(destination);
        var href = System.Text.Encodings.Web.HtmlEncoder.Default.Encode(destination);
        var label = System.Text.Encodings.Web.HtmlEncoder.Default.Encode(Text["NativeSignIn.ReturnToApp"]);
        return Content($"<!doctype html><html><head><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><meta name=\"referrer\" content=\"no-referrer\"><title>{label}</title></head><body><a href=\"{href}\" rel=\"noreferrer\">{label}</a><script nonce=\"{nonce}\">location.assign({encoded});</script></body></html>", "text/html");
    }
    private void Headers() => HelloUiSensitivePage.ApplyResponseHeaders(Response);
    private bool OriginValid()
    {
        var configured = HttpContext.RequestServices.GetRequiredService<IHelloSiteContext>().Current?.PublicOrigin;
        return configured is not null && Request.IsHttps && Request.Headers.Origin.Count == 1
            && Uri.TryCreate(Request.Headers.Origin.ToString(), UriKind.Absolute, out var origin)
            && origin.AbsolutePath == "/" && origin.Query.Length == 0 && origin.Fragment.Length == 0 && origin.UserInfo.Length == 0
            && origin.Scheme == configured.Scheme
            && origin.Authority.Equals(configured.Authority, StringComparison.OrdinalIgnoreCase);
    }
    private static CookieOptions CookieOptions(DateTimeOffset? expires = null) => new() { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, IsEssential = true, Path = "/", Expires = expires };
    private Binding? ReadBinding() { try { return Request.Cookies.TryGetValue(Cookie, out var value) ? JsonSerializer.Deserialize<Binding>(protector.Unprotect(value)) : null; } catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or JsonException) { return null; } }
    private sealed record Binding(string FlowId, string Binder);
}
