using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Skopka.Identity.ExternalLogins;

namespace Microsoft.Extensions.DependencyInjection;

public static class SkopkaHelloOidcServiceCollectionExtensions
{
    public static IServiceCollection AddSkopkaHelloOidc<TProfile>(
        this IServiceCollection services,
        Action<Skopka.Hello.Oidc.HelloOidcOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new Skopka.Hello.Oidc.HelloOidcOptions();
        configure(options);
        var registrations = options.Validate();
        var catalog = new Skopka.Hello.Oidc.HelloOidcProviderCatalog(
            registrations);

        services.AddSingleton(options);
        services.AddSingleton(catalog);
        services.AddSingleton<
            Skopka.Hello.Oidc.IHelloOidcProviderCatalog>(catalog);
        services.TryAddSingleton<
            Skopka.Hello.Oidc.IHelloOidcChallengeService,
            Skopka.Hello.Oidc.HelloOidcChallengeService>();
        services.TryAddSingleton<
            Skopka.Hello.Oidc.InMemoryHelloOidcFlowStore>();
        services.TryAddScoped<
            Skopka.Hello.Oidc.IHelloOidcFlowStore,
            Skopka.Hello.Oidc.HelloOidcFlowStore<TProfile>>();
        services.TryAddScoped<
            Skopka.Hello.Oidc.HelloOidcTicketService>();
        services.TryAddScoped<
            Skopka.Hello.Oidc.IHelloOidcApplication<TProfile>,
            Skopka.Hello.Oidc.HelloOidcApplication<TProfile>>();

        var authentication = services
            .AddAuthentication()
            .AddCookie(
                Skopka.Hello.Oidc.HelloOidcDefaults
                    .ExternalCookieScheme,
                cookie => ConfigureCookie(
                    cookie,
                    options.ExternalCookieName,
                    options.ExternalCookieLifetime,
                    SameSiteMode.Lax,
                    options.SecureCookies))
            .AddCookie(
                Skopka.Hello.Oidc.HelloOidcDefaults
                    .PendingCookieScheme,
                cookie => ConfigureCookie(
                    cookie,
                    options.PendingCookieName,
                    options.PendingCookieLifetime,
                    SameSiteMode.Strict,
                    options.SecureCookies));

        foreach (var provider in registrations)
        {
            authentication.AddOpenIdConnect(
                provider.AuthenticationScheme,
                provider.DisplayName,
                oidc => ConfigureProvider(
                    oidc,
                    provider,
                    options));
        }

        return services;
    }

    private static void ConfigureCookie(
        CookieAuthenticationOptions cookie,
        string name,
        TimeSpan lifetime,
        SameSiteMode sameSite,
        bool secure)
    {
        cookie.Cookie.Name = name;
        cookie.Cookie.HttpOnly = true;
        cookie.Cookie.IsEssential = true;
        cookie.Cookie.Path = "/";
        cookie.Cookie.SameSite = sameSite;
        cookie.Cookie.SecurePolicy = secure
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        cookie.ExpireTimeSpan = lifetime;
        cookie.SlidingExpiration = false;
    }

    private static void ConfigureProvider(
        OpenIdConnectOptions oidc,
        Skopka.Hello.Oidc.HelloOidcProviderRegistration provider,
        Skopka.Hello.Oidc.HelloOidcOptions helloOptions)
    {
        oidc.SignInScheme = Skopka.Hello.Oidc.HelloOidcDefaults
            .ExternalCookieScheme;
        oidc.Authority = provider.Authority;
        oidc.ClientId = provider.ClientId;
        oidc.ClientSecret = provider.ClientSecret;
        oidc.CallbackPath = provider.CallbackPath;
        oidc.ResponseType = OpenIdConnectResponseType.Code;
        oidc.UsePkce = true;
        oidc.SaveTokens = false;
        oidc.MapInboundClaims = false;
        oidc.GetClaimsFromUserInfoEndpoint = false;
        oidc.RequireHttpsMetadata = provider.RequireHttpsMetadata;
        oidc.RemoteAuthenticationTimeout =
            helloOptions.ExternalCookieLifetime;
        oidc.Scope.Clear();
        foreach (var scope in provider.Scopes)
        {
            oidc.Scope.Add(scope);
        }

        oidc.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            NameClaimType = Skopka.Hello.Oidc.HelloOidcClaims.Name,
        };
        oidc.CorrelationCookie.HttpOnly = true;
        oidc.CorrelationCookie.IsEssential = true;
        oidc.CorrelationCookie.SameSite = SameSiteMode.None;
        oidc.CorrelationCookie.SecurePolicy =
            CookieSecurePolicy.Always;
        oidc.NonceCookie.HttpOnly = true;
        oidc.NonceCookie.IsEssential = true;
        oidc.NonceCookie.SameSite = SameSiteMode.None;
        oidc.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;

        oidc.Events = new OpenIdConnectEvents
        {
            OnRedirectToIdentityProvider = context =>
            {
                ApplySensitiveRedirectHeaders(context.Response);
                var redirectUri = new Uri(
                    helloOptions.PublicOrigin!,
                    provider.CallbackPath.Value!.TrimStart('/'))
                    .AbsoluteUri;
                context.ProtocolMessage.RedirectUri = redirectUri;
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
                ValidatePrincipal(context, provider),
            OnTicketReceived = context =>
            {
                ApplySensitiveRedirectHeaders(context.Response);
                return Task.CompletedTask;
            },
            OnAccessDenied = context =>
                RedirectFailure(context),
            OnRemoteFailure = context =>
                RedirectFailure(context),
        };
    }

    private static Task ValidatePrincipal(
        TokenValidatedContext context,
        Skopka.Hello.Oidc.HelloOidcProviderRegistration provider)
    {
        var subjectClaims = context.Principal?
            .FindAll(Skopka.Hello.Oidc.HelloOidcClaims.Subject)
            .ToArray()
            ?? [];
        if (subjectClaims.Length != 1
            || string.IsNullOrWhiteSpace(subjectClaims[0].Value)
            || subjectClaims[0].Value.Length
                > ExternalLoginLimits.MaximumSubjectLength
            || context.Properties is null
            || !context.Properties.Items.TryGetValue(
                Skopka.Hello.Oidc.HelloOidcProperties.Provider,
                out var intendedProvider)
            || !string.Equals(
                intendedProvider,
                provider.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            context.Fail("The validated OIDC subject is invalid.");
            return Task.CompletedTask;
        }

        var identity = new ClaimsIdentity(
            provider.AuthenticationScheme,
            Skopka.Hello.Oidc.HelloOidcClaims.Name,
            roleType: null);
        identity.AddClaim(
            new Claim(
                Skopka.Hello.Oidc.HelloOidcClaims.Provider,
                provider.Id));
        identity.AddClaim(
            new Claim(
                Skopka.Hello.Oidc.HelloOidcClaims.Subject,
                subjectClaims[0].Value));
        CopySingleClaim(
            context.Principal!,
            identity,
            Skopka.Hello.Oidc.HelloOidcClaims.Name,
            200);
        CopySingleClaim(
            context.Principal!,
            identity,
            Skopka.Hello.Oidc.HelloOidcClaims.Email,
            320);
        CopyBooleanClaim(
            context.Principal!,
            identity,
            Skopka.Hello.Oidc.HelloOidcClaims.EmailVerified);
        CopySingleClaim(
            context.Principal!,
            identity,
            Skopka.Hello.Oidc.HelloOidcClaims.Locale,
            32);

        context.Principal = new ClaimsPrincipal(identity);
        return Task.CompletedTask;
    }

    private static void CopySingleClaim(
        ClaimsPrincipal source,
        ClaimsIdentity destination,
        string type,
        int maximumLength)
    {
        var claims = source.FindAll(type).ToArray();
        if (claims.Length == 1
            && !string.IsNullOrWhiteSpace(claims[0].Value)
            && claims[0].Value.Length <= maximumLength)
        {
            destination.AddClaim(new Claim(type, claims[0].Value));
        }
    }

    private static void CopyBooleanClaim(
        ClaimsPrincipal source,
        ClaimsIdentity destination,
        string type)
    {
        var claims = source.FindAll(type).ToArray();
        if (claims.Length == 1
            && bool.TryParse(claims[0].Value, out var value))
        {
            destination.AddClaim(
                new Claim(type, value.ToString()));
        }
    }

    private static async Task RedirectFailure(
        RemoteFailureContext context)
    {
        await ClearBrowserFlowAsync(context.HttpContext);
        context.HandleResponse();
        ApplySensitiveRedirectHeaders(context.Response);
        context.Response.Redirect(
            GetFailureRedirect(
                context.HttpContext,
                context.Properties));
    }

    private static async Task RedirectFailure(
        AccessDeniedContext context)
    {
        await ClearBrowserFlowAsync(context.HttpContext);
        context.HandleResponse();
        ApplySensitiveRedirectHeaders(context.Response);
        context.Response.Redirect(
            GetFailureRedirect(
                context.HttpContext,
                context.Properties));
    }

    private static string GetFailureRedirect(
        HttpContext httpContext,
        AuthenticationProperties? properties)
    {
        var uiRoutes = httpContext.RequestServices
            .GetRequiredService<Skopka.Hello.HelloUiRoutePaths>();
        return properties?.Items.TryGetValue(
                Skopka.Hello.Oidc.HelloOidcProperties.Intent,
                out var intent) == true
            && string.Equals(
                intent,
                Skopka.Hello.Oidc.HelloOidcProperties.LinkIntent,
                StringComparison.Ordinal)
                ? $"{uiRoutes.ExternalLoginsPath}?externalError=true"
                : $"{uiRoutes.LoginPath}?externalError=true";
    }

    private static async Task ClearBrowserFlowAsync(
        HttpContext httpContext)
    {
        await httpContext.SignOutAsync(
            Skopka.Hello.Oidc.HelloOidcDefaults.ExternalCookieScheme);
        await httpContext.SignOutAsync(
            Skopka.Hello.Oidc.HelloOidcDefaults.PendingCookieScheme);
    }

    private static void ApplySensitiveRedirectHeaders(
        HttpResponse response)
    {
        response.Headers.CacheControl = "no-store, max-age=0";
        response.Headers.Pragma = "no-cache";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    }
}
