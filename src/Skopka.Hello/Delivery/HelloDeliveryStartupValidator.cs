using Microsoft.Extensions.Hosting;

namespace Skopka.Hello;

internal sealed class HelloDeliveryStartupValidator(
    HelloAccountMessageDispatcher dispatcher)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = dispatcher;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
