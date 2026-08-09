using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Sessions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Skopka.Hello.AuthorizationServer;

public static class
    SkopkaHelloAuthorizationServerEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapSkopkaHelloAuthorizationServer<
        TProfile>(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetRequiredService<
            HelloAuthorizationServerOptions>();
        endpoints.MapGet(
                options.AuthorizationEndpointPath,
                AuthorizeAsync<TProfile>)
            .AllowAnonymous()
            .WithName("SkopkaHelloAuthorize");
        endpoints.MapPost(
                options.TokenEndpointPath,
                ExchangeAsync<TProfile>)
            .AllowAnonymous()
            .WithName("SkopkaHelloToken");
        return endpoints;
    }

    private static async Task<IResult> AuthorizeAsync<TProfile>(
        HttpContext httpContext,
        IHelloAuthorizationApplication<TProfile> application,
        HelloAuthorizationServerOptions options,
        CancellationToken cancellationToken)
    {
        var request = httpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException(
                "OpenIddict did not expose the authorization request.");

        if (!TryGetClientResource(
                request,
                options,
                out _,
                out var resource))
        {
            return Forbid(
                Errors.InvalidClient,
                "The authorization client is not configured.");
        }

        if (!HasAllowedResource(request, resource))
        {
            return Forbid(
                Errors.InvalidTarget,
                "The requested resource is not allowed for this client.");
        }

        if (HasPrompt(request, "consent"))
        {
            return Forbid(
                Errors.ConsentRequired,
                "Interactive third-party consent is not supported.");
        }

        if (HasPrompt(request, "select_account"))
        {
            return Forbid(
                Errors.InteractionRequired,
                "Account selection is not supported.");
        }

        var authentication = await httpContext.AuthenticateAsync(
            options.BrowserAuthenticationScheme);
        var forceLogin = HasPrompt(request, "login")
            || httpContext.Request.Query.ContainsKey("max_age");
        if (forceLogin && authentication.Succeeded)
        {
            await httpContext.SignOutAsync(
                options.BrowserAuthenticationScheme);
            return Results.Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = BuildLoginReturnUrl(httpContext),
                },
                [options.BrowserAuthenticationScheme]);
        }

        if (!authentication.Succeeded
            || authentication.Principal is null)
        {
            if (HasPrompt(request, "none"))
            {
                return Forbid(
                    Errors.LoginRequired,
                    "An active local sign-in is required.");
            }

            return Results.Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = BuildLoginReturnUrl(httpContext),
                },
                [options.BrowserAuthenticationScheme]);
        }

        if (!TryReadSession(
                authentication.Principal,
                out var userId,
                out var sourceSessionId))
        {
            return Forbid(
                Errors.LoginRequired,
                "The local sign-in is invalid.");
        }

        var validated = await application.ValidateAsync(
            userId,
            sourceSessionId,
            cancellationToken);
        if (!validated.IsSuccess)
        {
            return Forbid(
                Errors.LoginRequired,
                "The local sign-in is no longer valid.");
        }

        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType,
            Claims.Name,
            Claims.Role);
        identity.SetClaim(
            Claims.Subject,
            validated.Value.UserId.ToString("D"));
        identity.SetClaim(
            HelloAuthorizationDefaults.SourceSessionIdClaim,
            validated.Value.SessionId.ToString("D"));

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(request.GetScopes());
        principal.SetResources(resource);
        return Results.SignIn(
            principal,
            authenticationScheme:
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> ExchangeAsync<TProfile>(
        HttpContext httpContext,
        IHelloAuthorizationApplication<TProfile> application,
        HelloAuthorizationServerOptions options,
        CancellationToken cancellationToken)
    {
        var request = httpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException(
                "OpenIddict did not expose the token request.");
        if (!TryGetClientResource(
                request,
                options,
                out var client,
                out var resource))
        {
            return Forbid(
                Errors.InvalidClient,
                "The authorization client is not configured.");
        }

        if (!HasAllowedResource(request, resource))
        {
            return Forbid(
                Errors.InvalidTarget,
                "The requested resource is not allowed for this client.");
        }

        var authentication = await httpContext.AuthenticateAsync(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (!authentication.Succeeded
            || authentication.Principal is null
            || !TryReadUserId(
                authentication.Principal,
                out var userId))
        {
            return Forbid(
                Errors.InvalidGrant,
                "The token grant is invalid or expired.");
        }

        if (!HasOnlyResource(authentication.Principal, resource))
        {
            return Forbid(
                Errors.InvalidGrant,
                "The token grant is not bound to the configured resource.");
        }

        OperationResult<HelloAuthorizationSubject> result;
        if (request.IsAuthorizationCodeGrantType())
        {
            if (!Guid.TryParse(
                    authentication.Principal.FindFirstValue(
                        HelloAuthorizationDefaults.SourceSessionIdClaim),
                    out var sourceSessionId)
                || sourceSessionId == Guid.Empty)
            {
                return Forbid(
                    Errors.InvalidGrant,
                    "The authorization code is not bound to a local session.");
            }

            result = await application.CreateAsync(
                userId,
                sourceSessionId,
                client.DisplayName,
                cancellationToken);
        }
        else if (request.IsRefreshTokenGrantType())
        {
            if (!Guid.TryParse(
                    authentication.Principal.FindFirstValue(
                        IdentitySessionClaimTypes.SessionId),
                    out var sessionId)
                || sessionId == Guid.Empty)
            {
                return Forbid(
                    Errors.InvalidGrant,
                    "The refresh token is not bound to a logical session.");
            }

            result = await application.ValidateAsync(
                userId,
                sessionId,
                cancellationToken);
        }
        else
        {
            return Forbid(
                Errors.UnsupportedGrantType,
                "Only authorization-code and refresh-token grants are supported.");
        }

        if (!result.IsSuccess)
        {
            return Forbid(
                Errors.InvalidGrant,
                "The logical session is invalid or expired.");
        }

        var scopes = authentication.Principal.GetScopes();
        if (request.IsRefreshTokenGrantType()
            && request.GetScopes().Any())
        {
            scopes = request.GetScopes();
        }

        var principal = CreateTokenPrincipal(
            result.Value,
            scopes,
            resource);
        return Results.SignIn(
            principal,
            authenticationScheme:
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static ClaimsPrincipal CreateTokenPrincipal(
        HelloAuthorizationSubject subject,
        IEnumerable<string> scopes,
        string resource)
    {
        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType,
            Claims.Name,
            Claims.Role);
        identity.SetClaim(Claims.Subject, subject.UserId.ToString("D"));
        identity.SetClaim(
            IdentitySessionClaimTypes.SessionId,
            subject.SessionId.ToString("D"));
        identity.SetClaim(
            HelloAuthorizationDefaults.OAuthTransportClaim,
            HelloAuthorizationDefaults.OAuthTransport);
        foreach (var projected in subject.Claims)
        {
            if (ReservedClaimTypes.Contains(projected.Type))
            {
                continue;
            }

            var claim = new Claim(
                projected.Type,
                projected.Value,
                GetClaimValueType(projected.Type));
            if (SingletonClaimTypes.Contains(projected.Type))
            {
                foreach (var existing in identity.FindAll(projected.Type)
                    .ToArray())
                {
                    identity.RemoveClaim(existing);
                }

                identity.AddClaim(claim);
            }
            else
            {
                identity.AddClaim(claim);
            }
        }

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(scopes);
        principal.SetResources(resource);
        principal.SetDestinations(GetDestinations);
        return principal;
    }

    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        var principal = claim.Subject
            ?? throw new InvalidOperationException(
                "An authorization claim has no identity.");
        var scopes = new ClaimsPrincipal(principal).GetScopes();

        if (claim.Type is Claims.Subject
            or IdentitySessionClaimTypes.SessionId)
        {
            return
            [
                Destinations.AccessToken,
                Destinations.IdentityToken,
            ];
        }

        if (claim.Type == HelloAuthorizationDefaults.OAuthTransportClaim)
        {
            return [Destinations.AccessToken];
        }

        if (claim.Type is IdentitySessionClaimTypes.Name
            or IdentitySessionClaimTypes.PreferredUserName)
        {
            return scopes.Contains(Scopes.Profile)
                ? [Destinations.AccessToken, Destinations.IdentityToken]
                : [];
        }

        if (claim.Type is IdentitySessionClaimTypes.Email
            or IdentitySessionClaimTypes.EmailVerified)
        {
            return scopes.Contains(Scopes.Email)
                ? [Destinations.AccessToken, Destinations.IdentityToken]
                : [];
        }

        if (claim.Type is IdentitySessionClaimTypes.PhoneNumber
            or IdentitySessionClaimTypes.PhoneNumberVerified)
        {
            return scopes.Contains(Scopes.Phone)
                ? [Destinations.AccessToken, Destinations.IdentityToken]
                : [];
        }

        if (claim.Type == IdentitySessionClaimTypes.Role)
        {
            return scopes.Contains(HelloAuthorizationDefaults.RolesScope)
                ? [Destinations.AccessToken, Destinations.IdentityToken]
                : [];
        }

        return [Destinations.AccessToken];
    }

    private static string GetClaimValueType(string claimType)
        => claimType is IdentitySessionClaimTypes.EmailVerified
            or IdentitySessionClaimTypes.PhoneNumberVerified
                ? ClaimValueTypes.Boolean
                : ClaimValueTypes.String;

    private static readonly HashSet<string> ReservedClaimTypes = new(
        StringComparer.Ordinal)
    {
        Claims.Subject,
        IdentitySessionClaimTypes.SessionId,
        HelloAuthorizationDefaults.SourceSessionIdClaim,
        HelloAuthorizationDefaults.OAuthTransportClaim,
        "aud",
        "azp",
        "client_id",
        "exp",
        "iat",
        "iss",
        "jti",
        "nbf",
        "scope",
        "token_id",
    };

    private static readonly HashSet<string> SingletonClaimTypes = new(
        StringComparer.Ordinal)
    {
        IdentitySessionClaimTypes.Name,
        IdentitySessionClaimTypes.PreferredUserName,
        IdentitySessionClaimTypes.Email,
        IdentitySessionClaimTypes.EmailVerified,
        IdentitySessionClaimTypes.PhoneNumber,
        IdentitySessionClaimTypes.PhoneNumberVerified,
    };

    private static bool TryReadSession(
        ClaimsPrincipal principal,
        out Guid userId,
        out Guid sessionId)
    {
        sessionId = Guid.Empty;
        return TryReadUserId(principal, out userId)
            && Guid.TryParse(
                principal.FindFirstValue(
                    IdentitySessionClaimTypes.SessionId),
                out sessionId)
            && sessionId != Guid.Empty;
    }

    private static bool TryGetClientResource(
        OpenIddictRequest request,
        HelloAuthorizationServerOptions options,
        out HelloAuthorizationClientOptions client,
        out string resource)
    {
        client = options.Clients.FirstOrDefault(candidate => string.Equals(
            candidate.ClientId,
            request.ClientId,
            StringComparison.Ordinal))!;
        if (client is null)
        {
            resource = string.Empty;
            return false;
        }

        resource = options.GetResource(client);
        return true;
    }

    private static bool HasAllowedResource(
        OpenIddictRequest request,
        string configuredResource)
    {
        var requested = request.GetResources().ToArray();
        return requested.Length == 0
            || (requested.Length == 1
                && string.Equals(
                    requested[0],
                    configuredResource,
                    StringComparison.Ordinal));
    }

    private static bool HasOnlyResource(
        ClaimsPrincipal principal,
        string configuredResource)
    {
        var resources = principal.GetResources().ToArray();
        return resources.Length == 1
            && string.Equals(
                resources[0],
                configuredResource,
                StringComparison.Ordinal);
    }

    private static bool HasPrompt(
        OpenIddictRequest request,
        string prompt)
        => request.GetPromptValues().Contains(
            prompt,
            StringComparer.Ordinal);

    private static bool TryReadUserId(
        ClaimsPrincipal principal,
        out Guid userId)
        => Guid.TryParse(
                principal.FindFirstValue(Claims.Subject)
                    ?? principal.FindFirstValue(
                        ClaimTypes.NameIdentifier),
                out userId)
            && userId != Guid.Empty;

    private static string BuildLoginReturnUrl(HttpContext httpContext)
    {
        var query = httpContext.Request.Query
            .Where(parameter => parameter.Key is not "prompt" and not "max_age")
            .SelectMany(parameter => parameter.Value.Select(value =>
                new KeyValuePair<string, string?>(parameter.Key, value)));
        return httpContext.Request.PathBase
            + httpContext.Request.Path
            + QueryString.Create(query);
    }

    private static IResult Forbid(string error, string description)
        => Results.Forbid(
            new AuthenticationProperties(
                new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] =
                        error,
                    [OpenIddictServerAspNetCoreConstants.Properties
                        .ErrorDescription] = description,
                }),
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
}
