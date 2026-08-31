using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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
        services.AddProblemDetails();
        AddRegistrationConsentRequirement(services, options);
        AddLocalizationServices(services);

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
        services.AddProblemDetails();
        AddRegistrationConsentRequirement(services, options);
        AddLocalizationServices(services);

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
        if (typeof(
                Skopka.Hello
                    .IHelloRegistrationConsentProfileEnricher<TProfile>)
            .IsAssignableFrom(typeof(TProfileFactory)))
        {
            services.TryAddScoped<
                Skopka.Hello
                    .IHelloRegistrationConsentProfileEnricher<TProfile>>(
                provider =>
                    (Skopka.Hello
                        .IHelloRegistrationConsentProfileEnricher<TProfile>)
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
        services.TryAddScoped<
            Skopka.Hello.UI.IHelloUiUserAccessor,
            Skopka.Hello.UI.HelloUiUserAccessor>();
        services.TryAddScoped<
            Skopka.Hello.UI.IHelloUiAccountSwitcher,
            Skopka.Hello.UI.HelloUiAccountSwitcher<TProfile>>();
        var crossDeviceEnabled = services.LastOrDefault(descriptor =>
                descriptor.ServiceType
                    == typeof(Skopka.Hello.HelloCrossDeviceSignInOptions))
            ?.ImplementationInstance is
                Skopka.Hello.HelloCrossDeviceSignInOptions
        { Enabled: true };
        if (crossDeviceEnabled)
        {
            services.TryAddScoped<
                Skopka.Hello.UI.IHelloUiCrossDeviceApplication,
                Skopka.Hello.UI.HelloUiCrossDeviceApplication<TProfile>>();
        }

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
            .AddDataAnnotationsLocalization()
            .AddApplicationPart(
                typeof(Skopka.Hello.UI.HelloUiDefaults).Assembly);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IConfigureOptions<RazorPagesOptions>,
                Skopka.Hello.UI.HelloUiRazorPagesOptionsSetup>());

        return services;
    }

    private static void AddRegistrationConsentRequirement(
        IServiceCollection services,
        Skopka.Hello.UI.SkopkaHelloUiOptions options)
        => services.AddSingleton(
            new Skopka.Hello.HelloRegistrationConsentRequirement(
                options.TermsOfServiceUrl is not null,
                options.PrivacyPolicyUrl is not null));

    private static void AddLocalizationServices(
        IServiceCollection services)
    {
        services.AddAntiforgery();
        services.AddMemoryCache();
        services.TryAddSingleton<
            Skopka.Hello.UI.HelloUiPrgStateStore>();
        services.AddSingleton(
            new Skopka.Hello.UI.HelloUiDictionarySource(
                typeof(Skopka.Hello.UI.HelloUiModule).Assembly,
                [
                    "Skopka.Hello.UI.Localization.en.json",
                    "Skopka.Hello.UI.Localization.ru.json",
                ]));
        services.AddSingleton(
            new Skopka.Hello.UI.HelloUiLocalizationTarget(
                typeof(Skopka.Hello.UI.HelloUiModule).Assembly));
        services.TryAddSingleton<
            Skopka.Hello.UI.HelloUiTextCatalog>();
        services.TryAddSingleton<
            Skopka.Hello.UI.IHelloUiLocalizer,
            Skopka.Hello.UI.HelloUiLocalizer>();
        services.TryAddScoped<
            Skopka.Hello.UI.HelloUiRequestCultureFilter>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IPostConfigureOptions<
                    Microsoft.AspNetCore.Mvc.DataAnnotations
                        .MvcDataAnnotationsLocalizationOptions>,
                Skopka.Hello.UI
                    .HelloUiDataAnnotationsLocalizationSetup>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IHostedService,
                Skopka.Hello.UI
                    .HelloUiLocalizationStartupValidator>());
    }
}
