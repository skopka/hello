using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.ExternalLogins;

namespace Skopka.Hello.Oidc;

internal sealed record HelloOidcTicket(
    Guid FlowId,
    string Intent,
    ExternalLoginKey Login,
    string ReturnUrl,
    string? Name,
    string? VerifiedEmail,
    string? Locale,
    Guid? UserId,
    Guid? SessionId,
    Guid? ChallengeId,
    DateTimeOffset ExpiresAt);

internal sealed record HelloOidcLinkRequest(
    Guid FlowId,
    string ProviderId,
    string ReturnUrl,
    Guid UserId,
    Guid SessionId,
    DateTimeOffset ExpiresAt);

internal sealed class HelloOidcTicketService(
    HelloOidcOptions options,
    HelloUiRoutePaths uiRoutes)
{
    public Task<OperationResult<HelloOidcTicket>> ReadExternalAsync(
        HttpContext httpContext)
        => ReadAsync(
            httpContext,
            HelloOidcDefaults.ExternalCookieScheme,
            options.ExternalCookieLifetime);

    public Task<OperationResult<HelloOidcTicket>> ReadPendingAsync(
        HttpContext httpContext)
        => ReadAsync(
            httpContext,
            HelloOidcDefaults.PendingCookieScheme,
            options.PendingCookieLifetime);

    public async Task<OperationResult<HelloOidcLinkRequest>>
        ReadLinkRequestAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var authenticated = await httpContext.AuthenticateAsync(
            HelloOidcDefaults.LinkRequestCookieScheme);
        if (!authenticated.Succeeded
            || authenticated.Principal is null
            || authenticated.Properties is null
            || authenticated.Properties.IssuedUtc is not { } issuedUtc
            || authenticated.Properties.ExpiresUtc is not { } expiresUtc
            || expiresUtc <= DateTimeOffset.UtcNow
            || expiresUtc <= issuedUtc
            || expiresUtc - issuedUtc > options.ExternalCookieLifetime)
        {
            return InvalidLinkRequest();
        }

        var properties = authenticated.Properties.Items;
        if (!properties.TryGetValue(
                HelloOidcProperties.Provider,
                out var providerId)
            || string.IsNullOrWhiteSpace(providerId)
            || !properties.TryGetValue(
                HelloOidcProperties.ReturnUrl,
                out var configuredReturnUrl)
            || !HelloOidcReturnUrl.TryNormalizeHeadless(
                configuredReturnUrl,
                out var returnUrl)
            || ParseGuid(properties, HelloOidcProperties.FlowId)
                is not { } flowId
            || ParseGuid(properties, HelloOidcProperties.UserId)
                is not { } userId
            || ParseGuid(properties, HelloOidcProperties.SessionId)
                is not { } sessionId)
        {
            return InvalidLinkRequest();
        }

        var providerClaims = authenticated.Principal.FindAll(
                HelloOidcClaims.Provider)
            .ToArray();
        if (providerClaims.Length != 1
            || !string.Equals(
                providerId,
                providerClaims[0].Value,
                StringComparison.OrdinalIgnoreCase))
        {
            return InvalidLinkRequest();
        }

        return OperationResultFactory.Success(
            new HelloOidcLinkRequest(
                flowId,
                providerClaims[0].Value,
                returnUrl,
                userId,
                sessionId,
                expiresUtc));
    }

    public static async Task<bool> WriteLinkRequestAsync(
        HttpContext httpContext,
        HelloOidcLinkRequest request)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTimeOffset.UtcNow;
        if (request.FlowId == Guid.Empty
            || request.UserId == Guid.Empty
            || request.SessionId == Guid.Empty
            || request.ExpiresAt <= now)
        {
            return false;
        }

        var identity = new ClaimsIdentity(
            HelloOidcDefaults.LinkRequestCookieScheme);
        identity.AddClaim(
            new Claim(
                HelloOidcClaims.Provider,
                request.ProviderId));
        var properties = new AuthenticationProperties
        {
            AllowRefresh = false,
            IsPersistent = false,
            IssuedUtc = now,
            ExpiresUtc = request.ExpiresAt,
        };
        properties.Items[HelloOidcProperties.Provider] =
            request.ProviderId;
        properties.Items[HelloOidcProperties.ReturnUrl] =
            request.ReturnUrl;
        properties.Items[HelloOidcProperties.FlowId] =
            request.FlowId.ToString("D");
        properties.Items[HelloOidcProperties.UserId] =
            request.UserId.ToString("D");
        properties.Items[HelloOidcProperties.SessionId] =
            request.SessionId.ToString("D");

        await httpContext.SignInAsync(
            HelloOidcDefaults.LinkRequestCookieScheme,
            new ClaimsPrincipal(identity),
            properties);
        return true;
    }

    public async Task<bool> PromoteToPendingAsync(
        HttpContext httpContext,
        HelloOidcTicket ticket)
    {
        var now = DateTimeOffset.UtcNow;
        var written = await WritePendingAsync(
            httpContext,
            ticket with
            {
                FlowId = HelloOidcFlowId.Create(),
                ExpiresAt = now.Add(options.PendingCookieLifetime),
            });
        if (!written)
        {
            return false;
        }

        await httpContext.SignOutAsync(
            HelloOidcDefaults.ExternalCookieScheme);
        return true;
    }

    public static async Task<bool> WritePendingAsync(
        HttpContext httpContext,
        HelloOidcTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(ticket);

        var now = DateTimeOffset.UtcNow;
        if (ticket.FlowId == Guid.Empty || ticket.ExpiresAt <= now)
        {
            return false;
        }

        var identity = new ClaimsIdentity(
            HelloOidcDefaults.PendingCookieScheme);
        identity.AddClaim(
            new Claim(
                HelloOidcClaims.Provider,
                ticket.Login.Provider));
        identity.AddClaim(
            new Claim(
                HelloOidcClaims.Subject,
                ticket.Login.Subject));
        AddOptionalClaim(identity, HelloOidcClaims.Name, ticket.Name);
        AddOptionalClaim(
            identity,
            HelloOidcClaims.Email,
            ticket.VerifiedEmail);
        if (ticket.VerifiedEmail is not null)
        {
            identity.AddClaim(
                new Claim(
                    HelloOidcClaims.EmailVerified,
                    bool.TrueString));
        }

        AddOptionalClaim(identity, HelloOidcClaims.Locale, ticket.Locale);

        var properties = new AuthenticationProperties
        {
            AllowRefresh = false,
            IsPersistent = false,
            IssuedUtc = now,
            ExpiresUtc = ticket.ExpiresAt,
        };
        properties.Items[HelloOidcProperties.Intent] = ticket.Intent;
        properties.Items[HelloOidcProperties.Provider] =
            ticket.Login.Provider;
        properties.Items[HelloOidcProperties.ReturnUrl] =
            ticket.ReturnUrl;
        properties.Items[HelloOidcProperties.FlowId] =
            ticket.FlowId.ToString("D");
        if (ticket.UserId is { } userId)
        {
            properties.Items[HelloOidcProperties.UserId] =
                userId.ToString("D");
        }

        if (ticket.SessionId is { } sessionId)
        {
            properties.Items[HelloOidcProperties.SessionId] =
                sessionId.ToString("D");
        }

        if (ticket.ChallengeId is { } challengeId)
        {
            properties.Items[HelloOidcProperties.ChallengeId] =
                challengeId.ToString("D");
        }

        await httpContext.SignInAsync(
            HelloOidcDefaults.PendingCookieScheme,
            new ClaimsPrincipal(identity),
            properties);
        return true;
    }

    public static Task<bool> RotatePendingAsync(
        HttpContext httpContext,
        HelloOidcTicket ticket)
        => WritePendingAsync(
            httpContext,
            ticket with { FlowId = HelloOidcFlowId.Create() });

    public static Task DeleteExternalAsync(HttpContext httpContext)
        => httpContext.SignOutAsync(
            HelloOidcDefaults.ExternalCookieScheme);

    public static Task DeletePendingAsync(HttpContext httpContext)
        => httpContext.SignOutAsync(
            HelloOidcDefaults.PendingCookieScheme);

    public static Task DeleteLinkRequestAsync(HttpContext httpContext)
        => httpContext.SignOutAsync(
            HelloOidcDefaults.LinkRequestCookieScheme);

    private async Task<OperationResult<HelloOidcTicket>> ReadAsync(
        HttpContext httpContext,
        string authenticationScheme,
        TimeSpan maximumLifetime)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var authenticated = await httpContext.AuthenticateAsync(
            authenticationScheme);
        if (!authenticated.Succeeded
            || authenticated.Principal is null
            || authenticated.Properties is null
            || authenticated.Properties.IssuedUtc is not { } issuedUtc
            || authenticated.Properties.ExpiresUtc is not { } expiresUtc
            || expiresUtc <= DateTimeOffset.UtcNow
            || expiresUtc <= issuedUtc
            || expiresUtc - issuedUtc > maximumLifetime)
        {
            return Invalid();
        }

        var properties = authenticated.Properties.Items;
        if (!properties.TryGetValue(
                HelloOidcProperties.Intent,
                out var intent)
            || intent is not (
                HelloOidcProperties.SignInIntent
                or HelloOidcProperties.LinkIntent
                or HelloOidcProperties.UnlinkIntent)
            || !properties.TryGetValue(
                HelloOidcProperties.Provider,
                out var propertyProvider)
            || string.IsNullOrWhiteSpace(propertyProvider)
            || ParseGuid(properties, HelloOidcProperties.FlowId)
                is not { } flowId)
        {
            return Invalid();
        }

        var providerClaims = authenticated.Principal.FindAll(
                HelloOidcClaims.Provider)
            .ToArray();
        var subjectClaims = authenticated.Principal.FindAll(
                HelloOidcClaims.Subject)
            .ToArray();
        if (providerClaims.Length != 1
            || subjectClaims.Length != 1
            || !string.Equals(
                propertyProvider,
                providerClaims[0].Value,
                StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(subjectClaims[0].Value)
            || subjectClaims[0].Value.Length
                > ExternalLoginLimits.MaximumSubjectLength)
        {
            return Invalid();
        }

        var returnUrl = properties.TryGetValue(
            HelloOidcProperties.ReturnUrl,
            out var configuredReturnUrl)
            ? HelloOidcReturnUrl.Normalize(
                configuredReturnUrl,
                uiRoutes.AccountPath,
                uiRoutes.ExternalCompletionPath)
            : uiRoutes.AccountPath;
        var emailVerified = bool.TryParse(
            authenticated.Principal.FindFirstValue(
                HelloOidcClaims.EmailVerified),
            out var verified)
            && verified;
        var ticket = new HelloOidcTicket(
            flowId,
            intent,
            new ExternalLoginKey(
                providerClaims[0].Value,
                subjectClaims[0].Value),
            returnUrl,
            ReadBoundedClaim(
                authenticated.Principal,
                HelloOidcClaims.Name,
                200),
            emailVerified
                ? ReadBoundedClaim(
                    authenticated.Principal,
                    HelloOidcClaims.Email,
                    320)
                : null,
            ReadBoundedClaim(
                authenticated.Principal,
                HelloOidcClaims.Locale,
                32),
            ParseGuid(properties, HelloOidcProperties.UserId),
            ParseGuid(properties, HelloOidcProperties.SessionId),
            ParseGuid(properties, HelloOidcProperties.ChallengeId),
            expiresUtc);

        return OperationResultFactory.Success(ticket);
    }

    private static string? ReadBoundedClaim(
        ClaimsPrincipal principal,
        string type,
        int maximumLength)
    {
        var claims = principal.FindAll(type).ToArray();
        if (claims.Length != 1)
        {
            return null;
        }

        var value = claims[0].Value.Trim();
        return value.Length is > 0 && value.Length <= maximumLength
            ? value
            : null;
    }

    private static Guid? ParseGuid(
        IDictionary<string, string?> properties,
        string key)
        => properties.TryGetValue(key, out var value)
            && Guid.TryParse(value, out var parsed)
            && parsed != Guid.Empty
                ? parsed
                : null;

    private static void AddOptionalClaim(
        ClaimsIdentity identity,
        string type,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            identity.AddClaim(new Claim(type, value));
        }
    }

    private static OperationResult<HelloOidcTicket> Invalid()
        => OperationResultFactory.Fail<HelloOidcTicket>(
            HelloOidcErrors.PendingIdentityInvalid());

    private static OperationResult<HelloOidcLinkRequest>
        InvalidLinkRequest()
        => OperationResultFactory.Fail<HelloOidcLinkRequest>(
            HelloOidcErrors.PendingIdentityInvalid());
}
