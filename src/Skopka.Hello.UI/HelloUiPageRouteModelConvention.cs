using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Skopka.Hello.UI;

internal sealed class HelloUiPageRouteModelConvention(
    HelloUiRoutePaths routes,
    bool selfRegistrationEnabled,
    bool crossDeviceEnabled,
    bool accountSwitchingEnabled,
    HelloUiPages enabledPages,
    bool emailConfirmationEnabled,
    bool phoneConfirmationEnabled)
    : IPageRouteModelConvention
{
    private readonly Dictionary<string, string?> pageRoutes =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["/Pages/SkopkaHello/Index.cshtml"] =
                IsEnabled(enabledPages, HelloUiPages.Account)
                    ? routes.RootPath
                    : null,
            ["/Pages/SkopkaHello/Error.cshtml"] = routes.ErrorPath,
            ["/Pages/SkopkaHello/Login.cshtml"] =
                IsEnabled(enabledPages, HelloUiPages.Login)
                    ? routes.LoginPath
                    : null,
            ["/Pages/SkopkaHello/Accounts.cshtml"] =
                accountSwitchingEnabled
                && IsEnabled(enabledPages, HelloUiPages.Login)
                    ? routes.AccountsPath
                    : null,
            ["/Pages/SkopkaHello/Register.cshtml"] =
                selfRegistrationEnabled
                && IsEnabled(
                    enabledPages,
                    HelloUiPages.Registration)
                ? routes.RegisterPath
                : null,
            ["/Pages/SkopkaHello/ForgotPassword.cshtml"] =
                IsEnabled(
                    enabledPages,
                    HelloUiPages.PasswordRecovery)
                    ? routes.ForgotPasswordPath
                    : null,
            ["/Pages/SkopkaHello/ResetPassword.cshtml"] =
                IsEnabled(
                    enabledPages,
                    HelloUiPages.PasswordRecovery)
                    ? routes.ResetPasswordPath
                    : null,
            ["/Pages/SkopkaHello/ResendConfirmation.cshtml"] =
                IsEnabled(
                    enabledPages,
                    HelloUiPages.ContactConfirmation)
                && emailConfirmationEnabled
                    ? routes.ResendConfirmationPath
                    : null,
            ["/Pages/SkopkaHello/ResendPhoneConfirmation.cshtml"] =
                IsEnabled(
                    enabledPages,
                    HelloUiPages.ContactConfirmation)
                && phoneConfirmationEnabled
                    ? routes.ResendPhoneConfirmationPath
                    : null,
            ["/Pages/SkopkaHello/ConfirmEmail.cshtml"] =
                IsEnabled(
                    enabledPages,
                    HelloUiPages.ContactConfirmation)
                && emailConfirmationEnabled
                    ? routes.ConfirmEmailPath
                    : null,
            ["/Pages/SkopkaHello/ConfirmPhone.cshtml"] =
                IsEnabled(
                    enabledPages,
                    HelloUiPages.ContactConfirmation)
                && phoneConfirmationEnabled
                    ? routes.ConfirmPhonePath
                    : null,
            ["/Pages/SkopkaHello/External/Complete.cshtml"] =
                IsEnabled(
                    enabledPages,
                    HelloUiPages.ExternalIdentity)
                    ? routes.ExternalCompletionPath
                    : null,
            ["/Pages/SkopkaHello/External/Register.cshtml"] =
                selfRegistrationEnabled
                && IsEnabled(
                    enabledPages,
                    HelloUiPages.ExternalIdentity)
                ? routes.ExternalRegistrationPath
                : null,
            ["/Pages/SkopkaHello/Account/Index.cshtml"] =
                IsEnabled(enabledPages, HelloUiPages.Account)
                    ? routes.AccountPath
                    : null,
            ["/Pages/SkopkaHello/Account/Sessions.cshtml"] =
                IsEnabled(enabledPages, HelloUiPages.Sessions)
                    ? routes.SessionsPath
                    : null,
            ["/Pages/SkopkaHello/Account/ChangePassword.cshtml"] =
                IsEnabled(
                    enabledPages,
                    HelloUiPages.AccountSecurity)
                    ? routes.ChangePasswordPath
                    : null,
            ["/Pages/SkopkaHello/Account/Security.cshtml"] =
                IsEnabled(
                    enabledPages,
                    HelloUiPages.AccountSecurity)
                    ? routes.AccountSecurityPath
                    : null,
            ["/Pages/SkopkaHello/Account/ExternalLogins.cshtml"] =
                IsEnabled(
                    enabledPages,
                    HelloUiPages.ExternalIdentity)
                    ? routes.ExternalLoginsPath
                    : null,
            ["/Pages/SkopkaHello/CrossDevice/Waiting.cshtml"] =
                crossDeviceEnabled
                && IsEnabled(enabledPages, HelloUiPages.Login)
                    ? routes.CrossDeviceWaitingPath
                    : null,
            ["/Pages/SkopkaHello/CrossDevice/Requests.cshtml"] =
                crossDeviceEnabled
                && IsEnabled(enabledPages, HelloUiPages.Login)
                    ? routes.CrossDeviceRequestsPath
                    : null,
            ["/Pages/SkopkaHello/CrossDevice/Approve.cshtml"] =
                crossDeviceEnabled
                && IsEnabled(enabledPages, HelloUiPages.Login)
                    ? routes.CrossDeviceApprovalPath
                    : null,
        };

    private static bool IsEnabled(
        HelloUiPages enabledPages,
        HelloUiPages page)
        => (enabledPages & page) == page;

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
