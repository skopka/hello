using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Skopka.Hello.UI;

/// <summary>Request-local projection of the configured site and global page limits.</summary>
public sealed class HelloUiSite(IHelloSiteContext sites, SkopkaHelloUiOptions options)
{
    public HelloSite? Current => sites.Current;
    public bool IsEnabled(HelloUiPages page) => options.IsEnabled(page)
        && sites.Current is { } site && site.IsEnabled((HelloSiteFeatures)(int)page);
    public string AuthenticatedRedirect(HelloUiRoutePaths routes) => Current?.AuthenticatedRedirectPath
        ?? options.GetAuthenticatedRedirectPath(routes);
    public string? Home => Current?.ApplicationHomeUrl ?? options.ApplicationHomeUrl;
    public static string Redirect(HttpContext context, SkopkaHelloUiOptions options, HelloUiRoutePaths routes)
        => (context.RequestServices.GetService(typeof(HelloUiSite)) as HelloUiSite)?.AuthenticatedRedirect(routes)
            ?? options.GetAuthenticatedRedirectPath(routes);
    public static string? LogoutPath(HttpContext context, SkopkaHelloUiOptions? options)
        => (context.RequestServices.GetService(typeof(HelloUiSite)) as HelloUiSite)?.Logout ?? options?.PostLogoutRedirectPath;
    public string? Logout => Current?.PostLogoutRedirectPath ?? options.PostLogoutRedirectPath;
}

internal sealed class HelloUiSiteFilter(HelloUiSite site) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;
    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var path = context.ActionDescriptor.ViewEnginePath;
        var page = path switch
        {
            "/SkopkaHello/Error" => HelloUiPages.None,
            "/SkopkaHello/Login" or "/SkopkaHello/Accounts" or "/SkopkaHello/AccessLink" => HelloUiPages.Login,
            "/SkopkaHello/Register" => HelloUiPages.Registration,
            "/SkopkaHello/ForgotPassword" or "/SkopkaHello/ResetPassword" => HelloUiPages.PasswordRecovery,
            "/SkopkaHello/ResendConfirmation" or "/SkopkaHello/ResendPhoneConfirmation" or "/SkopkaHello/ConfirmEmail" or "/SkopkaHello/ConfirmPhone" => HelloUiPages.ContactConfirmation,
            "/SkopkaHello/Index" or "/SkopkaHello/Account/Index" => HelloUiPages.Account,
            "/SkopkaHello/Account/Sessions" => HelloUiPages.Sessions,
            "/SkopkaHello/Account/ChangePassword" or "/SkopkaHello/Account/Security" => HelloUiPages.AccountSecurity,
            "/SkopkaHello/Account/ExternalLogins" or "/SkopkaHello/External/Complete" or "/SkopkaHello/External/Register" => HelloUiPages.ExternalIdentity,
            _ when path.StartsWith("/SkopkaHello/CrossDevice/", StringComparison.Ordinal) => HelloUiPages.Login,
            _ when path.StartsWith("/SkopkaHello/NativeSignIn/", StringComparison.Ordinal) => HelloUiPages.Login,
            _ => HelloUiPages.None,
        };
        var current = site.Current;
        var handler = context.HandlerMethod?.Name ?? string.Empty;
        var allowed = current is not null && (page == HelloUiPages.None || site.IsEnabled(page));
        if (page == HelloUiPages.PasswordRecovery) allowed &= current?.PasswordSignInEnabled == true;
        if (path == "/SkopkaHello/AccessLink") allowed &= current?.PasswordSignInEnabled == true;
        if (path == "/SkopkaHello/Register") allowed &= current?.SelfRegistrationEnabled == true && current.PasswordSignInEnabled;
        if (path == "/SkopkaHello/External/Register") allowed &= current?.SelfRegistrationEnabled == true && site.IsEnabled(HelloUiPages.Registration);
        if (path.StartsWith("/SkopkaHello/CrossDevice/", StringComparison.Ordinal)) allowed &= current?.CrossDeviceSignInEnabled == true;
        if (path == "/SkopkaHello/Account/Index" && handler.Contains("PhoneBinding", StringComparison.OrdinalIgnoreCase))
            allowed &= current?.PhoneOtpEnabled == true;
        if (path == "/SkopkaHello/Account/ChangePassword") allowed &= current?.PasswordSignInEnabled == true;
        if (path == "/SkopkaHello/Account/ExternalLogins") allowed &= current?.ExternalSignInEnabled == true;
        if (path == "/SkopkaHello/Account/Security")
        {
            if (handler is "BeginSet" or "CompleteSet" or "BeginRemove" or "CompleteRemove")
                allowed &= current?.PasswordSignInEnabled == true;
            if (handler.Contains("TotpPhone", StringComparison.OrdinalIgnoreCase))
                allowed &= current?.PhoneOtpEnabled == true;
            if (handler.Contains("Passkey", StringComparison.OrdinalIgnoreCase))
                allowed &= current?.WebAuthnSignInEnabled == true;
            if (handler.Contains("TotpExternal", StringComparison.OrdinalIgnoreCase))
                allowed &= current?.ExternalSignInEnabled == true && site.IsEnabled(HelloUiPages.ExternalIdentity);
        }
        if (path == "/SkopkaHello/Login")
        {
            if (handler.StartsWith("PhoneRegister", StringComparison.OrdinalIgnoreCase)) allowed &= current?.SelfRegistrationEnabled == true && current.IsEnabled(HelloSiteFeatures.Registration);
            if (handler.StartsWith("Phone", StringComparison.OrdinalIgnoreCase)) allowed &= current?.PhoneOtpEnabled == true;
            if (handler.StartsWith("Passkey", StringComparison.OrdinalIgnoreCase)) allowed &= current?.WebAuthnSignInEnabled == true;
            if (handler.StartsWith("External", StringComparison.OrdinalIgnoreCase)) allowed &= current?.ExternalSignInEnabled == true && site.IsEnabled(HelloUiPages.ExternalIdentity);
            if (handler.StartsWith("CrossDevice", StringComparison.OrdinalIgnoreCase)) allowed &= current?.CrossDeviceSignInEnabled == true;
            if (handler.Length == 0 && HttpMethods.IsPost(context.HttpContext.Request.Method)) allowed &= current?.PasswordSignInEnabled == true;
        }
        if (!allowed) { context.Result = new NotFoundResult(); return; }
        await next();
    }
}
