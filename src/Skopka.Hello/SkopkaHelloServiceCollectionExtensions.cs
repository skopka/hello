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
        services.TryAddSingleton(
            new Skopka.Hello.HelloCrossDeviceSignInOptions());
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<
            Skopka.Hello.IHelloRegistrationConsentPolicy,
            Skopka.Hello.HelloRegistrationConsentPolicy>();
        services.AddSingleton(
            new Skopka.Hello.HelloUiRoutePaths(
                options.UiPathPrefix));
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
        services.AddSkopkaHelloDelivery();
        services.TryAddSingleton<
            Skopka.Hello.IHelloAnonymousAccountMessageInbox,
            Skopka.Hello.InMemoryHelloAnonymousAccountMessageInbox>();
        services.TryAddScoped<
            Skopka.Hello.HelloAnonymousAccountMessageRequester<TProfile>>();
        services.TryAddScoped<
            Skopka.Hello.HelloAnonymousAccountMessageProcessor<TProfile>>();
        services.TryAddScoped<
            Skopka.Hello.HelloRegistrationAdmission<TProfile>>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                Microsoft.Extensions.Hosting.IHostedService,
                Skopka.Hello.HelloAnonymousAccountMessageWorker<TProfile>>());
        services.TryAddSingleton<
            IIdentitySecurityEventObserver,
            Skopka.Hello.HelloIdentitySecurityEventObserver>();
        services.TryAddScoped<
            Skopka.Hello.IHelloIdentityApplication<TProfile>,
            Skopka.Hello.HelloIdentityApplication<TProfile>>();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<
                Skopka.Hello.IHelloAccessTokenValidator<TProfile>,
                Skopka.Hello.IdentityAccessTokenValidator<TProfile>>());
        services.TryAddScoped<
            Skopka.Hello.IHelloExternalIdentityApplication<TProfile>,
            Skopka.Hello.HelloExternalIdentityApplication<TProfile>>();
        // Registered whether or not passkeys are enabled: the application
        // answers "not enabled" itself, which is one place rather than a
        // missing service every caller has to be ready for.
        services.TryAddSingleton<Skopka.Hello.WebAuthn.HelloWebAuthnTickets>();
        services.TryAddSingleton<
            Skopka.Hello.WebAuthn.InMemoryHelloWebAuthnFlowStore>();
        services.TryAddSingleton<
            Skopka.Hello.WebAuthn.IHelloWebAuthnFlowStore,
            Skopka.Hello.WebAuthn.HelloWebAuthnFlowStore<TProfile>>();
        services.TryAddScoped<
            Skopka.Hello.WebAuthn.IHelloWebAuthnApplication<TProfile>,
            Skopka.Hello.WebAuthn.HelloWebAuthnApplication<TProfile>>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                Skopka.Hello.IHelloStepUpRequirementProvider<TProfile>,
                Skopka.Hello
                    .HelloAccountStepUpRequirementProvider<TProfile>>());
        services.TryAddScoped<
            Skopka.Hello.HelloStepUpMethodResolver<TProfile>>();
        services.TryAddScoped<
            Skopka.Hello.IHelloSessionCookieManager,
            Skopka.Hello.HelloSessionCookieManager>();
        services.TryAddScoped<
            Skopka.Hello.IHelloAntiforgeryTokenIssuer,
            Skopka.Hello.HelloAntiforgeryTokenIssuer>();

        return services
            .AddSkopkaIdentity<TProfile>()
            .AddStepUpAuthorization<
                Skopka.Hello.HelloStepUpPolicyProvider<TProfile>>();
    }
}
