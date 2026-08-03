using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

public static class SkopkaHelloDeliveryServiceCollectionExtensions
{
    public static IServiceCollection AddSkopkaHelloDelivery(
        this IServiceCollection services,
        Action<Skopka.Hello.HelloDeliveryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is null)
        {
            services.TryAddSingleton(
                new Skopka.Hello.HelloDeliveryOptions());
        }
        else
        {
            var options = new Skopka.Hello.HelloDeliveryOptions();
            configure(options);
            services.Replace(ServiceDescriptor.Singleton(options));
        }

        services.TryAddSingleton<
            Skopka.Hello.HelloAccountMessageDispatcher>();
        services.TryAddSingleton<
            Skopka.Hello.IHelloAccountMessageSender>(provider =>
                provider.GetRequiredService<
                    Skopka.Hello.HelloAccountMessageDispatcher>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IHostedService,
                Skopka.Hello.HelloDeliveryStartupValidator>());
        return services;
    }

    public static IServiceCollection AddSkopkaHelloEmailProvider<TProvider>(
        this IServiceCollection services)
        where TProvider : class, Skopka.Hello.IHelloAccountMessageProvider
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSkopkaHelloDelivery();
        services.TryAddSingleton<TProvider>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                Skopka.Hello.IHelloAccountMessageProvider,
                Skopka.Hello.HelloEmailProviderRegistration<TProvider>>());
        return services;
    }

    public static IServiceCollection AddSkopkaHelloSmsProvider<TProvider>(
        this IServiceCollection services)
        where TProvider : class, Skopka.Hello.IHelloAccountMessageProvider
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSkopkaHelloDelivery();
        services.TryAddSingleton<TProvider>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                Skopka.Hello.IHelloAccountMessageProvider,
                Skopka.Hello.HelloSmsProviderRegistration<TProvider>>());
        return services;
    }
}
