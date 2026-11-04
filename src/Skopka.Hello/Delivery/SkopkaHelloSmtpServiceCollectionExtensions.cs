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
        services.AddSingleton(serviceProvider =>
            new Skopka.Hello.HelloAccountEmailTextCatalog(
                options,
                serviceProvider.GetService<
                    Microsoft.Extensions.Hosting.IHostEnvironment>()));
        services.AddSingleton<
            Skopka.Hello.HelloAccountEmailTemplateRenderer>();
        services.TryAddScoped<Skopka.Hello.IHelloEmailTransport, Skopka.Hello.SmtpHelloEmailTransport>();
        services.AddSingleton<Skopka.Hello.SmtpHelloAccountMessageTransport>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            Microsoft.Extensions.Hosting.IHostedService,
            Skopka.Hello.HelloEmailTransportStartupValidator>());
        if (options.UseBackgroundQueue)
        {
            services.AddSingleton<
                Skopka.Hello.SmtpHelloAccountMessageQueue>();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    Skopka.Hello.IHelloAccountMessageProvider,
                    Skopka.Hello.QueuedSmtpHelloAccountMessageProvider>());
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    Microsoft.Extensions.Hosting.IHostedService,
                    Skopka.Hello.SmtpHelloAccountMessageWorker>());
        }
        else
        {
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    Skopka.Hello.IHelloAccountMessageProvider,
                    Skopka.Hello.DirectSmtpHelloAccountMessageProvider>());
        }

        return services;
    }
}
