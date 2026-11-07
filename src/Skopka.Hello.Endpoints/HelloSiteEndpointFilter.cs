using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Skopka.Hello.Endpoints;

internal sealed class HelloSiteEndpointFilter(IHelloSiteContext sites, SkopkaHelloOptions options) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (options.Sites.Count == 0) return next(context);
        var site = sites.Current;
        if (site is null) return ValueTask.FromResult<object?>(Results.NotFound());
        var name = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? "";
        var feature = Requirement(name);
        if (!site.IsEnabled(feature)
            || name.Contains("Phone", StringComparison.Ordinal) && !site.PhoneOtpEnabled
            || name.Contains("Passkey", StringComparison.Ordinal) && !site.WebAuthnSignInEnabled
            || name.Contains("CrossDevice", StringComparison.Ordinal) && !site.CrossDeviceSignInEnabled
            || name.Contains("External", StringComparison.Ordinal) && !site.ExternalSignInEnabled
            || (name.Contains("Register", StringComparison.Ordinal) || name.Contains("Registration", StringComparison.Ordinal))
                && !site.SelfRegistrationEnabled
            || (name.Contains("Password", StringComparison.Ordinal) || name is "SkopkaHelloLogin" or "SkopkaHelloRegister")
                && !site.PasswordSignInEnabled)
            return ValueTask.FromResult<object?>(Results.NotFound());
        return next(context);
    }

    private static HelloSiteFeatures Requirement(string name)
    {
        if (name.Contains("Register", StringComparison.Ordinal) || name.Contains("Registration", StringComparison.Ordinal))
            return HelloSiteFeatures.Registration;
        if (name.Contains("PasswordReset", StringComparison.Ordinal) || name == "SkopkaHelloResetPassword")
            return HelloSiteFeatures.PasswordRecovery;
        if (name.Contains("Confirmation", StringComparison.Ordinal) || name is "SkopkaHelloConfirmEmail" or "SkopkaHelloConfirmPhone")
            return HelloSiteFeatures.ContactConfirmation;
        if (name.Contains("External", StringComparison.Ordinal)) return HelloSiteFeatures.ExternalIdentity;
        if (name.Contains("Session", StringComparison.Ordinal)) return HelloSiteFeatures.Sessions;
        if (name.Contains("Password", StringComparison.Ordinal) || name.Contains("Authenticator", StringComparison.Ordinal)
            || name.Contains("Passkey", StringComparison.Ordinal) && !name.Contains("SignIn", StringComparison.Ordinal)
            || name.Contains("AccountDeletion", StringComparison.Ordinal)) return HelloSiteFeatures.AccountSecurity;
        if (name is "SkopkaHelloGetMe" or "SkopkaHelloChangeUserName" or "SkopkaHelloChangeEmail"
            or "SkopkaHelloReplaceProfile" || name.Contains("PhoneBinding", StringComparison.Ordinal)) return HelloSiteFeatures.Account;
        if (name.Contains("Logout", StringComparison.Ordinal) || name.Contains("Antiforgery", StringComparison.Ordinal))
            return HelloSiteFeatures.None;
        return HelloSiteFeatures.Login;
    }
}
