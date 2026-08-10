using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Skopka.Hello.UI;

internal sealed class HelloUiPageApplicationModelConvention
    : IPageApplicationModelConvention
{
    public void Apply(PageApplicationModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!model.RelativePath.StartsWith(
                "/Pages/SkopkaHello/",
                StringComparison.Ordinal)
            && !model.RelativePath.StartsWith(
                "/Pages/SkopkaHelloAdmin/",
                StringComparison.Ordinal))
        {
            return;
        }

        model.Filters.Add(
            new ServiceFilterAttribute(
                typeof(HelloUiRequestCultureFilter)));
    }
}
