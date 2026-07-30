using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skopka.Identity;
using Skopka.Identity.SecurityEvents;

namespace Microsoft.Extensions.DependencyInjection;

public static class SkopkaHelloServiceCollectionExtensions
{
    public static IdentityBuilder<TProfile> AddSkopkaHello<TProfile>(
        this IServiceCollection services,
        Action<Skopka.Hello.SkopkaHelloOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new Skopka.Hello.SkopkaHelloOptions();
        configure?.Invoke(options);
        options.Validate();

        services.AddSingleton(options);
        services.AddHttpContextAccessor();
        services.AddAntiforgery(antiforgery =>
        {
            antiforgery.HeaderName = options.AntiforgeryHeaderName;
            antiforgery.Cookie.Name = options.AntiforgeryCookieName;
            antiforgery.Cookie.HttpOnly = true;
            antiforgery.Cookie.IsEssential = true;
            antiforgery.Cookie.Path = "/";
            antiforgery.Cookie.SameSite = options.CookieSameSite;
            antiforgery.Cookie.SecurePolicy = options.SecureCookies
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.SameAsRequest;
        });

        services.TryAddSingleton<
            Skopka.Hello.IHelloRequestContext,
            Skopka.Hello.AspNetHelloRequestContext>();
        services.TryAddSingleton<
            Skopka.Hello.IHelloSecurityEventSink,
            Skopka.Hello.NullHelloSecurityEventSink>();
        services.TryAddSingleton<
            IIdentitySecurityEventObserver,
            Skopka.Hello.HelloIdentitySecurityEventObserver>();
        services.TryAddScoped<
            Skopka.Hello.IHelloIdentityApplication<TProfile>,
            Skopka.Hello.HelloIdentityApplication<TProfile>>();
        services.TryAddScoped<
            Skopka.Hello.IHelloSessionCookieManager,
            Skopka.Hello.HelloSessionCookieManager>();

        return services.AddSkopkaIdentity<TProfile>();
    }
}
