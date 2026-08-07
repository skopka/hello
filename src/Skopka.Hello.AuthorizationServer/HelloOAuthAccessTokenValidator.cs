using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;
using Skopka.Identity.Sessions;
using Skopka.Identity.Users;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Skopka.Hello.AuthorizationServer;

internal sealed class HelloOAuthAccessTokenValidator<TProfile>(
    IHttpContextAccessor httpContextAccessor,
    IIdentitySessionRegistry<TProfile> sessions)
    : IHelloAccessTokenValidator<TProfile>
{
    public async Task<OperationResult<IdentityUser<TProfile>>> ValidateAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null
            || !AuthenticationHeaderValue.TryParse(
                httpContext.Request.Headers.Authorization,
                out var authorization)
            || !string.Equals(
                authorization.Scheme,
                "Bearer",
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                authorization.Parameter,
                accessToken,
                StringComparison.Ordinal))
        {
            return Invalid();
        }

        var authentication = await httpContext.AuthenticateAsync(
            OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        var principal = authentication.Principal;
        if (!authentication.Succeeded
            || principal?.Identity?.IsAuthenticated != true
            || !string.Equals(
                principal.FindFirstValue(
                    HelloAuthorizationDefaults.OAuthTransportClaim),
                HelloAuthorizationDefaults.OAuthTransport,
                StringComparison.Ordinal)
            || !Guid.TryParse(
                principal.FindFirstValue(Claims.Subject),
                out var userId)
            || userId == Guid.Empty
            || !Guid.TryParse(
                principal.FindFirstValue(
                    IdentitySessionClaimTypes.SessionId),
                out var sessionId)
            || sessionId == Guid.Empty)
        {
            return Invalid();
        }

        return await sessions.ValidateAsync(
            new ValidateIdentitySessionCommand(userId, sessionId),
            cancellationToken);
    }

    private static OperationResult<IdentityUser<TProfile>> Invalid()
        => OperationResultFactory.Fail<IdentityUser<TProfile>>(
            new Error(
                IdentityErrorCodes.AccessTokenInvalid,
                "The access token is invalid or expired.",
                ErrorType.Unauthorized));
}
