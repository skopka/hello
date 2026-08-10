using Microsoft.Extensions.Hosting;

namespace Skopka.Hello.UI;

internal sealed class HelloUiLocalizationStartupValidator(
    IHelloUiLocalizer localizer)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = localizer.GetAllStrings(includeParentCultures: true).Count();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
