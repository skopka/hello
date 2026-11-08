using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QRCoder;

namespace Skopka.Hello.UI.Pages;

public sealed class AccessLinkModel(IHelloUiLocalizer text, HelloUiRoutePaths routes,
    IDataProtectionProvider protection, SkopkaHelloOptions options,
    HelloUiPrgStateStore prgState, TimeProvider clock) : PageModel
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
    /// <summary>Completion remains visible after the one-time recovery-code display.</summary>
    public bool Completed { get; private set; }
    public bool Enrolling => !string.IsNullOrEmpty(EnrollmentTicket);
    [TempData] public string? AccessLinkStateToken { get; set; }
    [TempData] public string? AccessLinkErrorsToken { get; set; }
    [TempData] public string? AccessLinkRecoveryCodesToken { get; set; }
    [TempData] public bool AccessLinkInvalid { get; set; }
    private sealed record Enrollment(Guid Id, string Secret, string TokenHash, DateTimeOffset ExpiresAt);
    private sealed record Flow(string Token, string EnrollmentTicket, DateTimeOffset ExpiresAt, bool Completed = false)
    {
        public override string ToString() => "AccessLinkFlow { [redacted] }";
    }
    private sealed record Errors(Dictionary<string, string[]> Messages);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private IDataProtector Protector => protection.CreateProtector("Skopka.Hello.UI.AccessLink.Enrollment.v1");
    public string LoginPath => routes.LoginPath;
    private IHelloUiAccessLinks? Service => HttpContext.RequestServices.GetService(typeof(IHelloUiAccessLinks)) as IHelloUiAccessLinks;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Sensitive();
        if (Service is null) return NotFound();
        if (prgState.TryGet<Flow>(AccessLinkStateToken, out var flow) && flow is not null)
        {
            TempData.Keep(nameof(AccessLinkStateToken));
            Completed = flow.Completed;
            if (Completed)
            {
                if (prgState.TryTake<string[]>(AccessLinkRecoveryCodesToken, out var codes)) RecoveryCodes = codes;
                AccessLinkRecoveryCodesToken = null;
            }
            else
            {
                Token = flow.Token;
                var inspection = await Service.InspectAsync(Token, cancellationToken);
                if (inspection.IsSuccess)
                {
                    Valid = true;
                    EnrollmentTicket = flow.EnrollmentTicket;
                    if (Enrolling)
                    {
                        var enrollment = ReadEnrollment();
                        if (enrollment is not null && enrollment.ExpiresAt > clock.GetUtcNow())
                        {
                            Secret = enrollment.Secret;
                            RenderQr();
                        }
                        else
                        {
                            EnrollmentTicket = string.Empty;
                            StoreFlow(new(Token, string.Empty, flow.ExpiresAt));
                            ModelState.AddModelError(string.Empty, text["AccessLink.EnrollmentExpired"]);
                        }
                    }
                }
                else
                {
                    ClearFlow();
                    AccessLinkInvalid = true;
                    ModelState.AddModelError(string.Empty, text["AccessLink.Invalid"]);
                }
            }
        }
        else if (!string.IsNullOrEmpty(AccessLinkStateToken))
        {
            ClearFlow();
            AccessLinkInvalid = true;
            ModelState.AddModelError(string.Empty, text["AccessLink.Invalid"]);
        }
        if (prgState.TryTake<Errors>(AccessLinkErrorsToken, out var errors) && errors is not null)
            foreach (var (key, messages) in errors.Messages)
                foreach (var message in messages) ModelState.AddModelError(key, message);
        AccessLinkErrorsToken = null;
        if (AccessLinkInvalid && ModelState.IsValid)
            ModelState.AddModelError(string.Empty, text["AccessLink.Invalid"]);
        if (AccessLinkInvalid) TempData.Keep(nameof(AccessLinkInvalid));
        return Page();
    }
    public async Task<IActionResult> OnPostInspectAsync(CancellationToken cancellationToken)
    {
        Sensitive();
        if (Service is null) return NotFound();
        ClearPrgState();
        ModelState.Clear();
        var result = await Service.InspectAsync(Token, cancellationToken);
        if (result.IsSuccess) StoreFlow(new(Token, string.Empty, FlowExpiry(result.Value.ExpiresAt)));
        else ModelState.AddModelError(string.Empty, text["AccessLink.Invalid"]);
        return RedirectWithErrors();
    }
    public async Task<IActionResult> OnPostBeginAsync(CancellationToken cancellationToken)
    {
        Sensitive();
        if (Service is null) return NotFound();
        ModelState.Clear();
        var result = await Service.BeginAsync(Token, HttpContext, cancellationToken);
        if (!result.IsSuccess)
        {
            ClearFlow();
            var inspection = await Service.InspectAsync(Token, cancellationToken);
            if (inspection.IsSuccess) StoreFlow(new(Token, string.Empty, FlowExpiry(inspection.Value.ExpiresAt)));
            HelloUiModelState.AddErrors(ModelState, result.Errors, text);
            return RedirectWithErrors();
        }
        EnrollmentTicket = Protector.Protect(JsonSerializer.Serialize(new Enrollment(
            result.Value.EnrollmentId, result.Value.Secret, Hash(Token), result.Value.ExpiresAt)));
        StoreFlow(new(Token, EnrollmentTicket, clock.GetUtcNow().AddMinutes(30)));
        return RedirectWithErrors();
    }
    public async Task<IActionResult> OnPostCompleteAsync(CancellationToken cancellationToken)
    {
        Sensitive();
        if (Service is null) return NotFound();
        var enrollment = ReadEnrollment();
        ModelState.Clear();
        if (enrollment is null)
        {
            ClearFlow();
            ModelState.AddModelError(string.Empty, text["AccessLink.Invalid"]);
            return RedirectWithErrors();
        }
        var inspection = await Service.InspectAsync(Token, cancellationToken);
        if (!inspection.IsSuccess)
        {
            ClearFlow();
            ModelState.AddModelError(string.Empty, text["AccessLink.Invalid"]);
            return RedirectWithErrors();
        }
        var expiresAt = FlowExpiry(inspection.Value.ExpiresAt);
        if (enrollment.ExpiresAt <= clock.GetUtcNow())
        {
            StoreFlow(new(Token, string.Empty, expiresAt));
            ModelState.AddModelError(string.Empty, text["AccessLink.EnrollmentExpired"]);
            return RedirectWithErrors();
        }
        StoreFlow(new(Token, EnrollmentTicket, expiresAt));
        if (Password != ConfirmPassword)
        {
            ModelState.AddModelError(nameof(ConfirmPassword), text["AccessLink.PasswordMismatch"]);
            return RedirectWithErrors();
        }
        var result = await Service.CompleteAsync(Token, enrollment.Id, Code, Password, HttpContext, cancellationToken);
        Password = ConfirmPassword = Code = string.Empty;
        ModelState.Clear();
        if (!result.IsSuccess)
        {
            inspection = await Service.InspectAsync(Token, cancellationToken);
            if (!inspection.IsSuccess)
            {
                ClearFlow();
                ModelState.AddModelError(string.Empty, text["AccessLink.Invalid"]);
            }
            else
            {
                foreach (var error in result.Errors)
                    if (error.Code == "identity.access_link.invalid")
                        ModelState.AddModelError(string.Empty, text["AccessLink.Retry"]);
                    else HelloUiModelState.AddErrors(ModelState, [error], text);
            }
            return RedirectWithErrors();
        }
        ClearPrgState();
        StoreFlow(new(string.Empty, string.Empty, clock.GetUtcNow().AddMinutes(10), Completed: true));
        AccessLinkRecoveryCodesToken = prgState.Store(result.Value.ToArray());
        return RedirectToPage("/SkopkaHello/AccessLink");
    }
    private Enrollment? ReadEnrollment()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(EnrollmentTicket) || EnrollmentTicket.Length > 4096
                || string.IsNullOrWhiteSpace(Token) || Token.Length > 512) return null;
            var enrollment = JsonSerializer.Deserialize<Enrollment>(Protector.Unprotect(EnrollmentTicket));
            return enrollment is not null && enrollment.TokenHash == Hash(Token)
                && !string.IsNullOrWhiteSpace(enrollment.Secret) && enrollment.Secret.Length <= 128
                && enrollment.Secret.All(c => c is (>= 'A' and <= 'Z') or (>= '2' and <= '7'))
                ? enrollment : null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or ArgumentException)
        {
            return null;
        }
    }
    private DateTimeOffset FlowExpiry(DateTimeOffset linkExpiry)
        => linkExpiry < clock.GetUtcNow().AddMinutes(30) ? linkExpiry : clock.GetUtcNow().AddMinutes(30);
    private void StoreFlow(Flow flow)
    {
        prgState.Remove(AccessLinkStateToken);
        AccessLinkInvalid = false;
        AccessLinkStateToken = prgState.Store(flow, flow.ExpiresAt);
    }
    private void ClearFlow()
    {
        prgState.Remove(AccessLinkStateToken);
        AccessLinkStateToken = null;
        EnrollmentTicket = Secret = string.Empty;
    }
    private void ClearPrgState()
    {
        ClearFlow();
        prgState.Remove(AccessLinkErrorsToken);
        prgState.Remove(AccessLinkRecoveryCodesToken);
        AccessLinkErrorsToken = AccessLinkRecoveryCodesToken = null;
        AccessLinkInvalid = false;
    }
    private RedirectToPageResult RedirectWithErrors()
    {
        Password = ConfirmPassword = Code = string.Empty;
        prgState.Remove(AccessLinkErrorsToken);
        var errors = ModelState.Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(entry => entry.Key, entry => entry.Value!.Errors.Select(error => error.ErrorMessage).ToArray());
        // Invalid anonymous links need no server-side cache entry. The cookie contains only this flag.
        AccessLinkInvalid = errors.Count > 0 && AccessLinkStateToken is null;
        AccessLinkErrorsToken = errors.Count == 0 || AccessLinkInvalid ? null : prgState.Store(new Errors(errors));
        ModelState.Clear();
        return RedirectToPage("/SkopkaHello/AccessLink");
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
