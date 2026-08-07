using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Validation.AspNetCore;

namespace Microsoft.Extensions.DependencyInjection;

public static class SkopkaHelloAuthorizationServerServiceCollectionExtensions
{
    public static IServiceCollection AddSkopkaHelloAuthorizationServer<TProfile>(
        this IServiceCollection services,
        Action<Skopka.Hello.AuthorizationServer.HelloAuthorizationServerOptions>
            configure,
        Action<OpenIddictServerBuilder>? configureServer = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = CreateOptions(configure);
        AddClientServices(services, options);
        services.TryAddScoped<
            Skopka.Hello.AuthorizationServer
                .IHelloAuthorizationApplication<TProfile>,
            Skopka.Hello.AuthorizationServer
                .HelloAuthorizationApplication<TProfile>>();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<
                Skopka.Hello.IHelloAccessTokenValidator<TProfile>,
                Skopka.Hello.AuthorizationServer
                    .HelloOAuthAccessTokenValidator<TProfile>>());
        services.AddAuthentication(authentication =>
            {
                authentication.DefaultAuthenticateScheme =
                    options.CompositeBearerAuthenticationScheme;
                authentication.DefaultChallengeScheme =
                    options.CompositeBearerAuthenticationScheme;
            })
            .AddScheme<
                AuthenticationSchemeOptions,
                Skopka.Hello.AuthorizationServer
                    .HelloOAuthSessionAuthenticationHandler<TProfile>>(
                Skopka.Hello.AuthorizationServer
                    .HelloAuthorizationDefaults.OAuthAuthenticationScheme,
                _ => { })
            .AddPolicyScheme(
                options.CompositeBearerAuthenticationScheme,
                displayName: null,
                policy => policy.ForwardDefaultSelector = context =>
                    SelectBearerScheme(context, options));

        services.AddOpenIddict()
            .AddServer(server =>
            {
                server.SetIssuer(options.Issuer!);
                server.SetAuthorizationEndpointUris(
                    options.GetOpenIddictAuthorizationEndpointPath());
                server.SetTokenEndpointUris(
                    options.GetOpenIddictTokenEndpointPath());
                server.AllowAuthorizationCodeFlow();
                server.AllowRefreshTokenFlow();
                server.RequireProofKeyForCodeExchange();
                server.RegisterScopes(
                    OpenIddictConstants.Scopes.OpenId,
                    OpenIddictConstants.Scopes.OfflineAccess,
                    OpenIddictConstants.Scopes.Profile,
                    OpenIddictConstants.Scopes.Email,
                    OpenIddictConstants.Scopes.Phone,
                    Skopka.Hello.AuthorizationServer
                        .HelloAuthorizationDefaults.RolesScope);
                server.SetAuthorizationCodeLifetime(
                    options.AuthorizationCodeLifetime);
                server.SetAccessTokenLifetime(options.AccessTokenLifetime);
                server.SetIdentityTokenLifetime(options.IdentityTokenLifetime);
                server.SetRefreshTokenLifetime(options.RefreshTokenLifetime);
                server.UseReferenceAccessTokens();
                server.UseReferenceRefreshTokens();

                configureServer?.Invoke(server);

                var aspNetCore = server.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough();
                if (options.DisableTransportSecurityRequirement)
                {
                    aspNetCore.DisableTransportSecurityRequirement();
                }
            })
            .AddValidation(validation =>
            {
                validation.UseLocalServer();
                validation.EnableTokenEntryValidation();
                validation.UseAspNetCore();
            });

        services.AddAuthorization(authorization =>
        {
            authorization.DefaultPolicy = new AuthorizationPolicyBuilder(
                    options.CompositeBearerAuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }

    private static string SelectBearerScheme(
        Microsoft.AspNetCore.Http.HttpContext context,
        Skopka.Hello.AuthorizationServer.HelloAuthorizationServerOptions
            options)
    {
        const string prefix = "Bearer ";
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return options.IdentityBearerAuthenticationScheme;
        }

        var token = header[prefix.Length..].Trim();
        return token.Count(character => character == '.') == 2
            ? options.IdentityBearerAuthenticationScheme
            : Skopka.Hello.AuthorizationServer.HelloAuthorizationDefaults
                .OAuthAuthenticationScheme;
    }

    public static IServiceCollection AddSkopkaHelloAuthorizationClients(
        this IServiceCollection services,
        Action<Skopka.Hello.AuthorizationServer.HelloAuthorizationServerOptions>
            configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = CreateOptions(configure);
        AddClientServices(services, options);

        return services;
    }

    private static Skopka.Hello.AuthorizationServer
        .HelloAuthorizationServerOptions CreateOptions(
            Action<Skopka.Hello.AuthorizationServer
                .HelloAuthorizationServerOptions> configure)
    {
        var options = new Skopka.Hello.AuthorizationServer
            .HelloAuthorizationServerOptions();
        configure(options);
        options.Validate();
        return options;
    }

    private static void AddClientServices(
        IServiceCollection services,
        Skopka.Hello.AuthorizationServer.HelloAuthorizationServerOptions
            options)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(
                Skopka.Hello.AuthorizationServer
                    .HelloAuthorizationServerOptions)))
        {
            throw new InvalidOperationException(
                "Skopka.Hello Authorization Server can only be configured once.");
        }

        services.AddSingleton(options);
        services.TryAddScoped<
            Skopka.Hello.AuthorizationServer
                .IHelloAuthorizationClientSynchronizer,
            Skopka.Hello.AuthorizationServer
                .HelloAuthorizationClientSynchronizer>();

    }
}
