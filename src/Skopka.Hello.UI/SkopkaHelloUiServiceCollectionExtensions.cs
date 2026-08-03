using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

public static class SkopkaHelloUiServiceCollectionExtensions
{
    public static IServiceCollection AddSkopkaHelloUi(
        this IServiceCollection services,
        Action<Skopka.Hello.UI.SkopkaHelloUiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new Skopka.Hello.UI.SkopkaHelloUiOptions();
        configure?.Invoke(options);
        options.Validate();
        services.AddSingleton(options);

        return services;
    }

    public static IServiceCollection AddSkopkaHelloUi<
        TProfile,
        TProfileFactory>(
        this IServiceCollection services,
        Action<Skopka.Hello.UI.SkopkaHelloUiOptions>? configure = null)
        where TProfileFactory : class,
            Skopka.Hello.UI.IHelloUiProfileFactory<TProfile>
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new Skopka.Hello.UI.SkopkaHelloUiOptions();
        configure?.Invoke(options);
        options.Validate();
        services.AddSingleton(options);

        services.TryAddScoped<TProfileFactory>();
        services.TryAddScoped<
            Skopka.Hello.UI.IHelloUiProfileFactory<TProfile>>(
            provider => provider.GetRequiredService<TProfileFactory>());
        if (typeof(Skopka.Hello.UI.IHelloUiProfileEditor<TProfile>)
            .IsAssignableFrom(typeof(TProfileFactory)))
        {
            services.TryAddScoped<
                Skopka.Hello.UI.IHelloUiProfileEditor<TProfile>>(
                provider =>
                    (Skopka.Hello.UI.IHelloUiProfileEditor<TProfile>)
                    provider.GetRequiredService<TProfileFactory>());
        }
        services.TryAddScoped<
            Skopka.Hello.UI.IHelloUiApplication,
            Skopka.Hello.UI.HelloUiApplication<TProfile>>();
        services.TryAddScoped<
            Skopka.Hello.UI.IHelloUiExternalApplication,
            Skopka.Hello.UI.HelloUiExternalApplication<TProfile>>();
        services.TryAddScoped<
            Skopka.Hello.UI.HelloUiCookieAuthenticationEvents<TProfile>>();

        services
            .AddAuthentication()
            .AddCookie(
                Skopka.Hello.UI.HelloUiDefaults.AuthenticationScheme,
                cookie =>
                {
                    cookie.Cookie.Name =
                        options.AuthenticationCookieName;
                    cookie.Cookie.HttpOnly = true;
                    cookie.Cookie.IsEssential = true;
                    cookie.Cookie.Path = "/";
                    cookie.Cookie.SameSite = options.CookieSameSite;
                    cookie.Cookie.SecurePolicy = options.SecureCookies
                        ? Microsoft.AspNetCore.Http.CookieSecurePolicy.Always
                        : Microsoft.AspNetCore.Http.CookieSecurePolicy
                            .SameAsRequest;
                    cookie.SlidingExpiration = false;
                    cookie.EventsType = typeof(
                        Skopka.Hello.UI
                            .HelloUiCookieAuthenticationEvents<TProfile>);
                });

        services
            .AddOptions<CookieAuthenticationOptions>(
                Skopka.Hello.UI.HelloUiDefaults.AuthenticationScheme)
            .Configure<Skopka.Hello.HelloUiRoutePaths>(
                (cookie, routes) =>
                {
                    cookie.LoginPath = routes.LoginPath;
                    cookie.AccessDeniedPath = routes.LoginPath;
                });

        services
            .AddAuthorizationBuilder()
            .AddPolicy(
                Skopka.Hello.UI.HelloUiDefaults.AuthorizationPolicy,
                policy =>
                {
                    policy.AddAuthenticationSchemes(
                        Skopka.Hello.UI.HelloUiDefaults
                            .AuthenticationScheme);
                    policy.RequireAuthenticatedUser();
                });

        services
            .AddRazorPages()
            .AddApplicationPart(
                typeof(Skopka.Hello.UI.HelloUiDefaults).Assembly);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IConfigureOptions<RazorPagesOptions>,
                Skopka.Hello.UI.HelloUiRazorPagesOptionsSetup>());

        return services;
    }
}
