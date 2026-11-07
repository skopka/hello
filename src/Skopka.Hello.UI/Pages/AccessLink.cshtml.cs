using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QRCoder;

namespace Skopka.Hello.UI.Pages;

public sealed class AccessLinkModel(IHelloUiLocalizer text, HelloUiRoutePaths routes, IDataProtectionProvider protection, SkopkaHelloOptions options) : PageModel
{
    [BindProperty] public string Token { get; set; } = string.Empty;
    [BindProperty] public string EnrollmentTicket { get; set; } = string.Empty;
    [BindProperty] public string Code { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    [BindProperty] public string ConfirmPassword { get; set; } = string.Empty;
    public string Secret { get; private set; } = string.Empty;
    public string? QrCode { get; private set; }
    public IReadOnlyList<string>? RecoveryCodes { get; private set; }
    public bool Valid { get; private set; }
    public bool Enrolling => !string.IsNullOrEmpty(EnrollmentTicket);
    private sealed record Enrollment(Guid Id, string Secret, string TokenHash);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private IDataProtector Protector => protection.CreateProtector("Skopka.Hello.UI.AccessLink.Enrollment.v1");
    public string LoginPath => routes.LoginPath;
    private IHelloUiAccessLinks? Service => HttpContext.RequestServices.GetService(typeof(IHelloUiAccessLinks)) as IHelloUiAccessLinks;

    public IActionResult OnGet()
    {
        Sensitive();
        return Service is null ? NotFound() : Page();
    }
    public async Task<IActionResult> OnPostInspectAsync(CancellationToken cancellationToken)
    {
        Sensitive();
        if (Service is null) return NotFound();
        var result = await Service.InspectAsync(Token, cancellationToken);
        Valid = result.IsSuccess;
        if (!Valid) ModelState.AddModelError(string.Empty, text["AccessLink.Invalid"]);
        return Page();
    }
    public async Task<IActionResult> OnPostBeginAsync(CancellationToken cancellationToken)
    {
        Sensitive();
        if (Service is null) return NotFound();
        var result = await Service.BeginAsync(Token, HttpContext, cancellationToken);
        if (!result.IsSuccess) { ModelState.AddModelError(string.Empty, text["AccessLink.Invalid"]); return Page(); }
        Secret = result.Value.Secret;
        EnrollmentTicket = Protector.Protect(JsonSerializer.Serialize(new Enrollment(result.Value.EnrollmentId, Secret, Hash(Token))));
        ModelState.Clear();
        RenderQr();
        Valid = true;
        return Page();
    }
    public async Task<IActionResult> OnPostCompleteAsync(CancellationToken cancellationToken)
    {
        Sensitive();
        if (Service is null) return NotFound();
        Enrollment? enrollment;
        try
        {
            enrollment = string.IsNullOrWhiteSpace(EnrollmentTicket) || EnrollmentTicket.Length > 4096
            || string.IsNullOrWhiteSpace(Token) || Token.Length > 512
            ? null : JsonSerializer.Deserialize<Enrollment>(Protector.Unprotect(EnrollmentTicket));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or ArgumentException) { enrollment = null; }
        if (enrollment is null || enrollment.TokenHash != Hash(Token) || enrollment.Secret.Length > 128
            || enrollment.Secret.Any(c => c is not (>= 'A' and <= 'Z') and not (>= '2' and <= '7')))
        {
            ModelState.Clear(); ModelState.AddModelError(string.Empty, text["AccessLink.Invalid"]); return Page();
        }
        Secret = enrollment.Secret;
        if (Password != ConfirmPassword)
        {
            Password = ConfirmPassword = Code = string.Empty;
            ModelState.Clear(); ModelState.AddModelError(string.Empty, text["AccessLink.PasswordMismatch"]);
            Valid = true; RenderQr(); return Page();
        }
        var result = await Service.CompleteAsync(Token, enrollment.Id, Code, Password, HttpContext, cancellationToken);
        Password = ConfirmPassword = Code = string.Empty;
        ModelState.Clear();
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(ModelState, result.Errors, text);
            var inspection = await Service.InspectAsync(Token, cancellationToken);
            Valid = inspection.IsSuccess;
            if (Valid) RenderQr();
            else ModelState.AddModelError(string.Empty, text["AccessLink.Invalid"]);
            return Page();
        }
        RecoveryCodes = result.Value;
        Token = string.Empty;
        Secret = string.Empty;
        return Page();
    }
    private void Sensitive()
    {
        HelloUiSensitivePage.ApplyResponseHeaders(Response);
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }
    private void RenderQr()
    {
        if (string.IsNullOrWhiteSpace(Secret)) return;
        var issuer = Uri.EscapeDataString(options.Totp.Issuer);
        var uri = $"otpauth://totp/{issuer}:account?secret={Uri.EscapeDataString(Secret)}&issuer={issuer}&algorithm=SHA1&digits=6&period=30";
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        using var qr = new PngByteQRCode(data);
        QrCode = "data:image/png;base64," + Convert.ToBase64String(qr.GetGraphic(5));
    }
}
