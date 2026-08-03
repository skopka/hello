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
        Assert.Contains("/identity/hello/login", routes);
        Assert.Contains("/identity/hello/register", routes);
        Assert.Contains(
            "/identity/hello/resend-phone-confirmation",
            routes);
        Assert.Contains(
            "/identity/hello/confirm-phone",
            routes);
        Assert.Contains(
            "/auth/phone-confirmation/request",
            routes);
        Assert.Contains(
            "/auth/phone-confirmation/confirm",
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
            selfRegistrationEnabled: true);
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
        bool selfRegistrationEnabled)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSkopkaHello<TestProfile>(options =>
        {
            options.UiPathPrefix = pathPrefix;
            options.SelfRegistrationEnabled =
                selfRegistrationEnabled;
        });
        builder.Services.AddSkopkaHelloUi<
            TestProfile,
            TestProfileFactory>();

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
