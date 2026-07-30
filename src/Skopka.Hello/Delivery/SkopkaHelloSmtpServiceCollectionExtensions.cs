using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class SkopkaHelloSmtpServiceCollectionExtensions
{
    public static IServiceCollection AddSkopkaHelloSmtpDelivery(
        this IServiceCollection services,
        Action<Skopka.Hello.HelloSmtpOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new Skopka.Hello.HelloSmtpOptions();
        configure(options);
        options.Validate();

        services.Replace(
            ServiceDescriptor.Singleton(options));
        services.Replace(
            ServiceDescriptor.Singleton<
                Skopka.Hello.IHelloAccountMessageSender,
                Skopka.Hello.QueuedHelloAccountMessageSender>());
        services.AddSingleton<
            Skopka.Hello.HelloAccountMessageQueue>();
        services.AddSingleton<
            Skopka.Hello.SmtpHelloAccountMessageTransport>();
        services.AddHostedService<
            Skopka.Hello.SmtpHelloAccountMessageWorker>();
        return services;
    }
}
