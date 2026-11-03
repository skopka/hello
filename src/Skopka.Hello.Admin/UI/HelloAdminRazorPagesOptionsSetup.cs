using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Skopka.Hello.Admin;

internal sealed class HelloAdminRazorPagesOptionsSetup(
    HelloAdminRoutePaths routes, Microsoft.Extensions.DependencyInjection.IServiceProviderIsService services)
    : IConfigureOptions<RazorPagesOptions>
{
    public void Configure(RazorPagesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Conventions.Add(
            new HelloAdminPageRouteConvention(routes, services.IsService(typeof(Skopka.Hello.AuthorizationServer.IHelloAuthorizationClientManagement))));
    }
}

internal sealed class HelloAdminPageRouteConvention(
    HelloAdminRoutePaths routes, bool clientsEnabled)
    : IPageRouteModelConvention
{
    private const string UsersPage =
        "/Pages/SkopkaHelloAdmin/Users.cshtml";

    private const string RolesPage =
        "/Pages/SkopkaHelloAdmin/Roles.cshtml";

    public void Apply(PageRouteModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.RelativePath == "/Pages/SkopkaHelloAdmin/Clients.cshtml" && !clientsEnabled) { model.Selectors.Clear(); return; }
        var route = model.RelativePath switch
        {
            UsersPage => routes.UsersPath,
            RolesPage => routes.RolesPath,
            "/Pages/SkopkaHelloAdmin/Clients.cshtml" => routes.ClientsPath,
            _ => null,
        };
        if (route is null)
        {
            return;
        }

        model.Selectors.Clear();
        model.Selectors.Add(
            new SelectorModel
            {
                AttributeRouteModel = new AttributeRouteModel
                {
                    Template = route.TrimStart('/'),
                },
            });
    }
}
