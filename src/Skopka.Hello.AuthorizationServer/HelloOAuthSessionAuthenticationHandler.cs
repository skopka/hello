using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Validation.AspNetCore;
using Skopka.Identity.Sessions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Skopka.Hello.AuthorizationServer;

internal sealed class HelloOAuthSessionAuthenticationHandler<TProfile>(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IIdentitySessionRegistry<TProfile> sessions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(
        options,
        logger,
        encoder)
{
    protected override async Task<AuthenticateResult>
        HandleAuthenticateAsync()
    {
        var authentication = await Context.AuthenticateAsync(
            OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        var principal = authentication.Principal;
        if (!authentication.Succeeded || principal is null)
        {
            return authentication.None
                ? AuthenticateResult.NoResult()
                : AuthenticateResult.Fail(
                    authentication.Failure
                    ?? new InvalidOperationException(
                        "The OAuth access token is invalid."));
        }

        if (!Guid.TryParse(
                principal.FindFirstValue(Claims.Subject),
                out var userId)
            || userId == Guid.Empty
            || !Guid.TryParse(
                principal.FindFirstValue(
                    IdentitySessionClaimTypes.SessionId),
                out var sessionId)
            || sessionId == Guid.Empty)
        {
            return AuthenticateResult.Fail(
                "The OAuth access token is not bound to a logical session.");
        }

        var validated = await sessions.ValidateAsync(
            new ValidateIdentitySessionCommand(userId, sessionId),
            Context.RequestAborted);
        if (!validated.IsSuccess)
        {
            return AuthenticateResult.Fail(
                "The OAuth logical session is invalid or expired.");
        }

        return AuthenticateResult.Success(
            new AuthenticationTicket(principal, Scheme.Name));
    }
}
