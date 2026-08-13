using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace Skopka.Hello.UI;

public sealed record HelloUiUser(
    Guid UserId,
    Guid SessionId,
    string DisplayName);

public interface IHelloUiUserAccessor
{
    Task<HelloUiUser?> GetAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken);
}

internal sealed class HelloUiUserAccessor : IHelloUiUserAccessor
{
    public async Task<HelloUiUser?> GetAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        cancellationToken.ThrowIfCancellationRequested();

        var authentication = await httpContext.AuthenticateAsync(
            HelloUiDefaults.AuthenticationScheme);
        cancellationToken.ThrowIfCancellationRequested();
        if (!authentication.Succeeded
            || authentication.Principal is null
            || !HelloUiPrincipalFactory.TryGetUserId(
                authentication.Principal,
                out var userId)
            || !HelloUiPrincipalFactory.TryGetSessionId(
                authentication.Principal,
                out var sessionId))
        {
            return null;
        }

        var displayName = authentication.Principal.FindFirstValue(
            HelloUiPrincipalFactory.DisplayNameClaim);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = authentication.Principal.Identity?.Name;
        }

        return string.IsNullOrWhiteSpace(displayName)
            ? null
            : new HelloUiUser(userId, sessionId, displayName);
    }
}
