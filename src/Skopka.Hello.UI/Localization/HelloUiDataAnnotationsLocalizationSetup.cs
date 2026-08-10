using Microsoft.AspNetCore.Mvc.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Skopka.Hello.UI;

internal sealed class HelloUiDataAnnotationsLocalizationSetup(
    IHelloUiLocalizer localizer,
    IEnumerable<HelloUiLocalizationTarget> targets)
    : IPostConfigureOptions<MvcDataAnnotationsLocalizationOptions>
{
    private readonly HashSet<System.Reflection.Assembly> targetAssemblies =
        targets.Select(target => target.Assembly).ToHashSet();

    public void PostConfigure(
        string? name,
        MvcDataAnnotationsLocalizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var fallback = options.DataAnnotationLocalizerProvider
            ?? ((modelType, factory) => factory.Create(modelType));
        options.DataAnnotationLocalizerProvider =
            (modelType, factory) =>
                targetAssemblies.Contains(modelType.Assembly)
                    ? localizer
                    : fallback(modelType, factory);
    }
}
