using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

public static class SkopkaHelloAdminServiceCollectionExtensions
{
    public static IServiceCollection AddSkopkaHelloAdmin<
        TProfile,
        TProfileProjector>(
        this IServiceCollection services,
        Action<Skopka.Hello.Admin.SkopkaHelloAdminOptions>? configure = null)
        where TProfileProjector : class,
            Skopka.Hello.Admin.IHelloAdminProfileProjector<TProfile>
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new Skopka.Hello.Admin.SkopkaHelloAdminOptions();
        configure?.Invoke(options);
        options.Validate();
        services.AddSingleton(options);

        services.TryAddScoped<TProfileProjector>();
        services.TryAddScoped<
            Skopka.Hello.Admin.IHelloAdminProfileProjector<TProfile>>(
            provider => provider.GetRequiredService<TProfileProjector>());
        services.TryAddScoped<
            Skopka.Hello.Admin.IHelloAdminApplication,
            Skopka.Hello.Admin.HelloAdminApplication<TProfile>>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                Skopka.Hello.IHelloStepUpRequirementProvider<TProfile>,
                Skopka.Hello.Admin
                    .HelloAdminStepUpRequirementProvider<TProfile>>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<
                IAuthorizationHandler,
                Skopka.Hello.Admin
                    .HelloAdminCurrentRoleHandler<TProfile>>());

        var authorization = services.AddAuthorizationBuilder();
        AddRolePolicy(
            authorization,
            options.ReadPolicyName,
            options.ReadRoleName);
        AddRolePolicy(
            authorization,
            options.ManagePolicyName,
            options.ManageRoleName);
        AddRolePolicy(
            authorization,
            options.DeletePolicyName,
            options.DeleteRoleName);

        if (options.RazorUiEnabled)
        {
            if (!services.Any(descriptor => descriptor.ServiceType
                    == typeof(Skopka.Hello.UI.IHelloUiApplication)))
            {
                throw new InvalidOperationException(
                    "AddSkopkaHelloUi<TProfile, TProfileFactory> must be called before enabling the admin Razor UI.");
            }

            services.AddSingleton(provider =>
                new Skopka.Hello.Admin.HelloAdminRoutePaths(
                    provider.GetRequiredService<
                        Skopka.Hello.HelloUiRoutePaths>(),
                    options));
            services
                .AddRazorPages()
                .AddApplicationPart(
                    typeof(Skopka.Hello.Admin.HelloAdminDefaults)
                        .Assembly);
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    IConfigureOptions<RazorPagesOptions>,
                    Skopka.Hello.Admin
                        .HelloAdminRazorPagesOptionsSetup>());
        }

        return services;
    }

    private static void AddRolePolicy(
        AuthorizationBuilder authorization,
        string policyName,
        string roleName)
        => authorization.AddPolicy(
            policyName,
            policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(
                    new Skopka.Hello.Admin
                        .HelloAdminCurrentRoleRequirement(roleName));
            });
}
