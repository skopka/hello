using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Skopka.Hello.Admin;

internal sealed class HelloAdminRazorPagesOptionsSetup(
    HelloAdminRoutePaths routes)
    : IConfigureOptions<RazorPagesOptions>
{
    public void Configure(RazorPagesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Conventions.Add(
            new HelloAdminPageRouteConvention(routes));
    }
}

internal sealed class HelloAdminPageRouteConvention(
    HelloAdminRoutePaths routes)
    : IPageRouteModelConvention
{
    private const string UsersPage =
        "/Pages/SkopkaHelloAdmin/Users.cshtml";

    public void Apply(PageRouteModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!string.Equals(
                model.RelativePath,
                UsersPage,
                StringComparison.Ordinal))
        {
            return;
        }

        model.Selectors.Clear();
        model.Selectors.Add(
            new SelectorModel
            {
                AttributeRouteModel = new AttributeRouteModel
                {
                    Template = routes.UsersPath.TrimStart('/'),
                },
            });
    }
}
