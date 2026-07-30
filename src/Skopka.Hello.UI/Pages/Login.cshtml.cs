using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Skopka.Hello.UI.Pages;

public sealed class LoginModel(
    IHelloUiApplication application,
    IHelloSessionCookieManager sessionCookies)
    : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public bool Registered { get; private set; }

    public async Task<IActionResult> OnGetAsync(bool registered)
    {
        var authentication = await HttpContext.AuthenticateAsync(
            HelloUiDefaults.AuthenticationScheme);
        if (authentication.Succeeded)
        {
            return Redirect(HelloUiDefaults.AccountPath);
        }

        Registered = registered;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(
        CancellationToken cancellationToken)
    {
        var transport = sessionCookies.ValidateTransport(HttpContext);
        if (!transport.IsSuccess)
        {
            HelloUiModelState.AddErrors(
                ModelState,
                transport.Errors);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await application.LoginAsync(
            new HelloUiLoginCommand(
                Input.Handle,
                Input.Login,
                Input.Password),
            HttpContext,
            cancellationToken);
        if (!result.IsSuccess)
        {
            HelloUiModelState.AddErrors(
                ModelState,
                result.Errors);
            return Page();
        }

        sessionCookies.WriteSessionCookies(
            HttpContext,
            result.Value.Session);
        await HttpContext.SignInAsync(
            HelloUiDefaults.AuthenticationScheme,
            result.Value.Principal,
            HelloUiAuthenticationProperties.Create(
                result.Value.Session));

        return Url.IsLocalUrl(ReturnUrl)
            ? LocalRedirect(ReturnUrl)
            : Redirect(HelloUiDefaults.AccountPath);
    }

    public sealed class InputModel
    {
        [Required]
        [Display(Name = "Sign in with")]
        public string Handle { get; set; } = "email";

        [Required]
        [StringLength(320)]
        [Display(Name = "Email or user name")]
        public string Login { get; set; } = string.Empty;

        [Required]
        [StringLength(128)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }
}
