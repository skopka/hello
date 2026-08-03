using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class SkopkaHelloSmtpServiceCollectionExtensions
{
    public static IServiceCollection AddSkopkaHelloSmtpProvider(
        this IServiceCollection services,
        Action<Skopka.Hello.HelloSmtpOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new Skopka.Hello.HelloSmtpOptions();
        configure(options);
        options.Validate();

        services.AddSkopkaHelloDelivery();
        services.Replace(
            ServiceDescriptor.Singleton(options));
        services.AddSingleton<
            Skopka.Hello.SmtpHelloAccountMessageQueue>();
        services.AddSingleton<
            Skopka.Hello.SmtpHelloAccountMessageTransport>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                Skopka.Hello.IHelloAccountMessageProvider,
                Skopka.Hello.QueuedSmtpHelloAccountMessageProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                Microsoft.Extensions.Hosting.IHostedService,
                Skopka.Hello.SmtpHelloAccountMessageWorker>());
        return services;
    }
}
