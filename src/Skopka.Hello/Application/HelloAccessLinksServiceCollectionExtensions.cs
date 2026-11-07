using Microsoft.Extensions.DependencyInjection.Extensions;
using Skopka.Hello;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers host-issued access-link orchestration; Identity must provide its access-link service.</summary>
public static class HelloAccessLinksServiceCollectionExtensions
{
    public static IServiceCollection AddHelloAccessLinks<TProfile>(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IHelloAccessLinks<TProfile>, HelloAccessLinks<TProfile>>();
        return services;
    }
}
