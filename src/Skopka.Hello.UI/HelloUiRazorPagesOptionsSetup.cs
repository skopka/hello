using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Skopka.Hello.UI;

internal sealed class HelloUiRazorPagesOptionsSetup(
    HelloUiRoutePaths routes,
    SkopkaHelloOptions helloOptions)
    : IConfigureOptions<RazorPagesOptions>
{
    public void Configure(RazorPagesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Conventions.Add(
            new HelloUiPageRouteModelConvention(
                routes,
                helloOptions.SelfRegistrationEnabled));
    }
}
