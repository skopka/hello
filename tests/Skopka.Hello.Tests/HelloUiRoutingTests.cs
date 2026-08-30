using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Skopka.Abstraction.OperationResult;
using Skopka.Hello.Endpoints;
using Skopka.Hello.UI;

namespace Skopka.Hello.Tests;

public sealed class HelloUiRoutingTests
{
    [Fact]
    public async Task CustomPrefixReplacesDefaultRoutesAndCookieRedirects()
    {
        await using var application = CreateApplication(
            "/identity/hello",
            selfRegistrationEnabled: true);

        var routes = GetRoutes(application);

        Assert.Contains("/auth/register", routes);
        Assert.Contains("/identity/hello", routes);
        Assert.Contains("/identity/hello/error", routes);
        Assert.Contains("/identity/hello/login", routes);
        Assert.Contains("/identity/hello/register", routes);
        Assert.Contains(
            "/identity/hello/forgot-password",
            routes);
        Assert.Contains(
            "/identity/hello/reset-password",
            routes);
        Assert.Contains(
            "/identity/hello/resend-confirmation",
            routes);
        Assert.Contains(
            "/identity/hello/resend-phone-confirmation",
            routes);
        Assert.Contains(
            "/identity/hello/confirm-email",
            routes);
        Assert.Contains(
            "/identity/hello/confirm-phone",
            routes);
        Assert.Contains(
            "/identity/hello/account",
            routes);
        Assert.Contains(
            "/identity/hello/account/sessions",
            routes);
        Assert.Contains(
            "/identity/hello/account/change-password",
            routes);
        Assert.Contains(
            "/identity/hello/account/security",
            routes);
        Assert.Contains(
            "/identity/hello/account/external-logins",
            routes);
        Assert.Contains(
            "/auth/phone-confirmation/request",
            routes);
        Assert.Contains(
            "/auth/phone-confirmation/confirm",
            routes);
        Assert.Contains(
            "/identity/hello/external/complete",
            routes);
        Assert.Contains(
            "/identity/hello/external/register",
            routes);
        Assert.DoesNotContain("/Login", routes);
        Assert.DoesNotContain("/Register", routes);
        Assert.DoesNotContain("/Account/Index", routes);
        Assert.DoesNotContain("/SkopkaHello/Index", routes);
        Assert.DoesNotContain(
            routes,
            route => route == "/hello"
                || route.StartsWith(
                    "/hello/",
                    StringComparison.Ordinal));

        var configuredRoutes = application.Services
            .GetRequiredService<HelloUiRoutePaths>();
        Assert.Equal("/identity/hello", configuredRoutes.RootPath);
        Assert.Equal(
            "/identity/hello/reset-password",
            configuredRoutes.ResetPasswordPath);
        Assert.Equal(
            "/identity/hello/confirm-phone",
            configuredRoutes.ConfirmPhonePath);
        Assert.Equal(
            "/identity/hello/account/security",
            configuredRoutes.AccountSecurityPath);
        Assert.Equal(
            "/identity/hello/error",
            configuredRoutes.ErrorPath);

        var cookie = application.Services
            .GetRequiredService<
                IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(HelloUiDefaults.AuthenticationScheme);
        Assert.Equal("/identity/hello/login", cookie.LoginPath);
        Assert.Equal("/identity/hello/login", cookie.AccessDeniedPath);
        Assert.Equal("/", cookie.Cookie.Path);
    }

    [Fact]
    public async Task DisabledSelfRegistrationRemovesApiAndUiRoutes()
    {
        await using var application = CreateApplication(
            "/identity",
            selfRegistrationEnabled: false);

        var routes = GetRoutes(application);

        Assert.DoesNotContain("/auth/register", routes);
        Assert.DoesNotContain("/identity/register", routes);
        Assert.DoesNotContain("/identity/external/register", routes);
        Assert.Contains("/auth/login", routes);
        Assert.Contains("/identity/login", routes);
        Assert.Contains("/identity/external/complete", routes);
        Assert.DoesNotContain("/auth/cross-device", routes);
        Assert.DoesNotContain("/identity/cross-device", routes);
        Assert.DoesNotContain("/identity/cross-device/approve", routes);
    }

    [Fact]
    public async Task EnabledCrossDeviceSignInPublishesApiAndUiRoutes()
    {
        await using var application = CreateApplication(
            "/identity",
            selfRegistrationEnabled: false,
            crossDeviceEnabled: true);

        var routes = GetRoutes(application);

        Assert.Contains("/auth/cross-device", routes);
        Assert.Contains(
            "/auth/cross-device/{deviceCode}/status",
            routes);
        Assert.Contains(
            "/auth/cross-device/{deviceCode}/complete",
            routes);
        Assert.Contains(
            "/account/cross-device/{deviceCode}",
            routes);
        Assert.Contains(
            "/account/cross-device/{deviceCode}/challenge",
            routes);
        Assert.Contains(
            "/account/cross-device/{deviceCode}/approve",
            routes);
        Assert.Contains(
            "/account/cross-device/{deviceCode}/deny",
            routes);
        Assert.Contains("/identity/cross-device", routes);
        Assert.Contains("/identity/cross-device/approve", routes);
    }

    [Fact]
    public async Task LoginOnlyStillPublishesErrorPageRoute()
    {
        await using var application = CreateApplication(
            "/identity",
            selfRegistrationEnabled: true,
            ui =>
            {
                ui.EnabledPages = HelloUiPages.Login;
                ui.AuthenticatedRedirectPath = "/admin";
            });

        var routes = GetRoutes(application)
            .Where(route =>
                route == "/identity"
                || route.StartsWith(
                    "/identity/",
                    StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(
            ["/identity/error", "/identity/login"],
            routes.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task DisabledPhoneConfirmationPublishesNoPhoneUiRoutes()
    {
        await using var application = CreateApplication(
            "/identity",
            selfRegistrationEnabled: false,
            ui => ui.ContactConfirmation.PhoneEnabled = false);

        var routes = GetRoutes(application);

        Assert.Contains("/identity/resend-confirmation", routes);
        Assert.Contains("/identity/confirm-email", routes);
        Assert.DoesNotContain(
            "/identity/resend-phone-confirmation",
            routes);
        Assert.DoesNotContain("/identity/confirm-phone", routes);
    }

    [Fact]
    public void RegistrationUiRequiresUrlForCoreConsentPolicy()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => CreateApplication(
                "/identity",
                selfRegistrationEnabled: true,
                configureHello: options =>
                    options.RegistrationConsent
                        .TermsOfServiceRequired = true));

        Assert.Contains(
            nameof(SkopkaHelloUiOptions.TermsOfServiceUrl),
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    public void RootPrefixIsRejected(string pathPrefix)
    {
        Assert.Throws<InvalidOperationException>(
            () => CreateApplication(
                pathPrefix,
                selfRegistrationEnabled: true));
    }

    [Fact]
    public void RouteConventionOnlyRewritesHelloRazorPages()
    {
        var convention = new HelloUiPageRouteModelConvention(
            new HelloUiRoutePaths("/identity"),
            selfRegistrationEnabled: true,
            crossDeviceEnabled: false,
            enabledPages: HelloUiPages.All,
            emailConfirmationEnabled: true,
            phoneConfirmationEnabled: true);
        var hostPage = CreatePageRouteModel(
            "/Pages/Index.cshtml",
            "/Index",
            "host-home");
        var helloPage = CreatePageRouteModel(
            "/Pages/SkopkaHello/Index.cshtml",
            "/SkopkaHello/Index",
            "SkopkaHello/Index");

        convention.Apply(hostPage);
        convention.Apply(helloPage);

        Assert.Equal(
            "host-home",
            Assert.Single(hostPage.Selectors)
                .AttributeRouteModel?.Template);
        Assert.Equal(
            "identity",
            Assert.Single(helloPage.Selectors)
                .AttributeRouteModel?.Template);
    }

    private static WebApplication CreateApplication(
        string pathPrefix,
        bool selfRegistrationEnabled,
        Action<SkopkaHelloUiOptions>? configureUi = null,
        Action<SkopkaHelloOptions>? configureHello = null,
        bool crossDeviceEnabled = false)
    {
        var builder = WebApplication.CreateBuilder();
        var hello = builder.Services.AddSkopkaHello<TestProfile>(options =>
        {
            options.UiPathPrefix = pathPrefix;
            options.SelfRegistrationEnabled =
                selfRegistrationEnabled;
            if (crossDeviceEnabled)
            {
                options.PublicOrigin = new Uri(
                    "https://accounts.example.test");
                options.Totp.Enabled = true;
            }

            configureHello?.Invoke(options);
        });
        if (crossDeviceEnabled)
        {
            hello.AddCrossDeviceSignIn();
        }

        builder.Services.AddSkopkaHelloUi<
            TestProfile,
            TestProfileFactory>(configureUi);

        var application = builder.Build();
        application.MapSkopkaHello<TestProfile>();
        application.MapSkopkaHelloUi();
        return application;
    }

    private static HashSet<string> GetRoutes(
        WebApplication application)
        => ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint =>
            {
                var route = endpoint.RoutePattern.RawText
                    ?? string.Empty;
                return "/" + route.TrimStart('/');
            })
            .ToHashSet(StringComparer.Ordinal);

    private static PageRouteModel CreatePageRouteModel(
        string relativePath,
        string viewEnginePath,
        string template)
    {
        var model = new PageRouteModel(
            relativePath,
            viewEnginePath);
        model.Selectors.Add(
            new SelectorModel
            {
                AttributeRouteModel = new AttributeRouteModel
                {
                    Template = template,
                },
            });
        return model;
    }

    private sealed record TestProfile(
        string DisplayName,
        string? Locale);

    private sealed class TestProfileFactory
        : IHelloUiProfileFactory<TestProfile>
    {
        public OperationResult<TestProfile> Create(
            HelloUiRegistrationProfile profile)
            => OperationResultFactory.Success(
                new TestProfile(
                    profile.DisplayName,
                    profile.Locale));

        public string GetDisplayName(TestProfile profile)
            => profile.DisplayName;
    }
}
