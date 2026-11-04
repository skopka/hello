using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Skopka.Hello;

internal sealed class HelloEmailTransportStartupValidator(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = scopeFactory.CreateAsyncScope();
        if (scope.ServiceProvider.GetRequiredService<IHelloEmailTransport>() is SmtpHelloEmailTransport)
        {
            var options = scope.ServiceProvider.GetRequiredService<HelloSmtpOptions>();
            ArgumentException.ThrowIfNullOrWhiteSpace(options.Host);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
