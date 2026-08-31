using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace Skopka.Hello.AuthorizationServer;

public interface IHelloAuthorizationSessionTerminator
{
    Task TerminateAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken);
}

internal sealed class HelloAuthorizationSessionTerminator<TProfile>(
    IHelloIdentityApplication<TProfile> identity,
    IHelloSessionCookieManager sessionCookies,
    HelloAuthorizationServerOptions options)
    : IHelloAuthorizationSessionTerminator
{
    public async Task TerminateAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var refreshToken = sessionCookies.ReadRefreshToken(httpContext);
        if (refreshToken is not null)
        {
            await identity.LogoutAsync(refreshToken, cancellationToken);
        }

        sessionCookies.DeleteSessionCookies(httpContext);
        await httpContext.SignOutAsync(
            options.BrowserAuthenticationScheme);
    }
}
