using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class SkopkaHelloEmailTransportServiceCollectionExtensions
{
    /// <summary>Replaces the default email transport with a scoped implementation.</summary>
    public static IServiceCollection AddSkopkaHelloEmailTransport<TTransport>(
        this IServiceCollection services)
        where TTransport : class, Skopka.Hello.IHelloEmailTransport
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(ServiceDescriptor.Scoped<Skopka.Hello.IHelloEmailTransport, TTransport>());
        return services;
    }
}
