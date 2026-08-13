using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class SkopkaHelloAuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddSkopkaHelloCurrentRolePolicy<
        TProfile>(
        this IServiceCollection services,
        string policyName,
        string roleName,
        string? authenticationScheme = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);
        if (authenticationScheme is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(
                authenticationScheme);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<
                IAuthorizationHandler,
                Skopka.Hello.HelloCurrentRoleHandler<TProfile>>());
        services
            .AddAuthorizationBuilder()
            .AddPolicy(
                policyName,
                policy =>
                {
                    if (authenticationScheme is not null)
                    {
                        policy.AddAuthenticationSchemes(
                            authenticationScheme);
                    }

                    policy.RequireAuthenticatedUser();
                    policy.AddRequirements(
                        new Skopka.Hello.HelloCurrentRoleRequirement(
                            roleName));
                });

        return services;
    }
}
