using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Skopka.Hello.UI;

internal sealed class HelloUiPageRouteModelConvention(
    HelloUiRoutePaths routes,
    bool selfRegistrationEnabled)
    : IPageRouteModelConvention
{
    private readonly Dictionary<string, string?> pageRoutes =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["/Pages/SkopkaHello/Index.cshtml"] = routes.RootPath,
            ["/Pages/SkopkaHello/Login.cshtml"] = routes.LoginPath,
            ["/Pages/SkopkaHello/Register.cshtml"] =
                selfRegistrationEnabled
                ? routes.RegisterPath
                : null,
            ["/Pages/SkopkaHello/ForgotPassword.cshtml"] =
                routes.ForgotPasswordPath,
            ["/Pages/SkopkaHello/ResetPassword.cshtml"] =
                routes.ResetPasswordPath,
            ["/Pages/SkopkaHello/ResendConfirmation.cshtml"] =
                routes.ResendConfirmationPath,
            ["/Pages/SkopkaHello/ResendPhoneConfirmation.cshtml"] =
                routes.ResendPhoneConfirmationPath,
            ["/Pages/SkopkaHello/ConfirmEmail.cshtml"] =
                routes.ConfirmEmailPath,
            ["/Pages/SkopkaHello/ConfirmPhone.cshtml"] =
                routes.ConfirmPhonePath,
            ["/Pages/SkopkaHello/External/Complete.cshtml"] =
                routes.ExternalCompletionPath,
            ["/Pages/SkopkaHello/External/Register.cshtml"] =
                selfRegistrationEnabled
                ? routes.ExternalRegistrationPath
                : null,
            ["/Pages/SkopkaHello/Account/Index.cshtml"] =
                routes.AccountPath,
            ["/Pages/SkopkaHello/Account/Sessions.cshtml"] =
                routes.SessionsPath,
            ["/Pages/SkopkaHello/Account/ChangePassword.cshtml"] =
                routes.ChangePasswordPath,
            ["/Pages/SkopkaHello/Account/Security.cshtml"] =
                routes.AccountSecurityPath,
            ["/Pages/SkopkaHello/Account/ExternalLogins.cshtml"] =
                routes.ExternalLoginsPath,
        };

    public void Apply(PageRouteModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!pageRoutes.TryGetValue(
                model.RelativePath,
                out var route))
        {
            return;
        }

        model.Selectors.Clear();
        if (route is null)
        {
            return;
        }

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
