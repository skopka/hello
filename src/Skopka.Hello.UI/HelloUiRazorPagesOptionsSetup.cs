using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Skopka.Hello.UI;

internal sealed class HelloUiRazorPagesOptionsSetup(
    HelloUiRoutePaths routes,
    SkopkaHelloOptions helloOptions,
    SkopkaHelloUiOptions uiOptions)
    : IConfigureOptions<RazorPagesOptions>
{
    public void Configure(RazorPagesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        uiOptions.ValidateRoutes(routes);

        options.Conventions.Add(
            new HelloUiPageRouteModelConvention(
                routes,
                helloOptions.SelfRegistrationEnabled,
                uiOptions.EnabledPages));
    }
}
