using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Skopka.Hello.UI.Pages;

internal static class HelloUiSession
{
    public static async Task EstablishAsync(
        HttpContext httpContext,
        IHelloSessionCookieManager sessionCookies,
        HelloUiSignIn signIn)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(sessionCookies);
        ArgumentNullException.ThrowIfNull(signIn);

        sessionCookies.WriteSessionCookies(
            httpContext,
            signIn.Session);
        httpContext.RequestServices
            .GetService<IHelloUiAccountSwitcher>()
            ?.Save(
                httpContext,
                signIn.Principal,
                signIn.Session);
        await httpContext.SignInAsync(
            HelloUiDefaults.AuthenticationScheme,
            signIn.Principal,
            HelloUiAuthenticationProperties.Create(
                signIn.Session));
    }
}
