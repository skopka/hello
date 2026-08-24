using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Hello.UI;
using Skopka.Identity.Errors;

namespace Skopka.Hello.Tests;

public sealed class SkopkaHelloUiOptionsTests
{
    [Fact]
    public void DefaultsKeepEveryUiPageEnabled()
    {
        var options = new SkopkaHelloUiOptions();

        options.Validate();

        Assert.Equal(HelloUiPages.All, options.EnabledPages);
        Assert.Null(options.AuthenticatedRedirectPath);
        Assert.Null(options.ApplicationHomeUrl);
        Assert.Null(options.TermsOfServiceUrl);
        Assert.Null(options.PrivacyPolicyUrl);
        Assert.Equal(
            HelloUiRegistrationFieldMode.Hidden,
            options.Registration.Locale);
        Assert.Equal(
            HelloUiRegistrationFieldMode.Required,
            options.Registration.DisplayName);
    }

    [Fact]
    public void LoginOnlyRequiresAuthenticatedRedirectPath()
    {
        var options = new SkopkaHelloUiOptions
        {
            EnabledPages = HelloUiPages.Login,
        };

        var exception = Assert.Throws<InvalidOperationException>(
            options.Validate);

        Assert.Contains(
            nameof(SkopkaHelloUiOptions.AuthenticatedRedirectPath),
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HelloUiPages.Registration)]
    [InlineData(HelloUiPages.PasswordRecovery)]
    [InlineData(HelloUiPages.ContactConfirmation)]
    [InlineData(HelloUiPages.Account)]
    public void UiFeaturesRequireLoginPage(HelloUiPages feature)
    {
        var options = new SkopkaHelloUiOptions
        {
            EnabledPages = feature,
        };

        var exception = Assert.Throws<InvalidOperationException>(
            options.Validate);

        Assert.Contains(
            nameof(HelloUiPages.Login),
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HelloUiPages.Sessions)]
    [InlineData(HelloUiPages.AccountSecurity)]
    [InlineData(HelloUiPages.ExternalIdentity)]
    public void AccountFeaturesRequireAccountPage(
        HelloUiPages accountFeature)
    {
        var options = new SkopkaHelloUiOptions
        {
            EnabledPages = HelloUiPages.Login | accountFeature,
            AuthenticatedRedirectPath = "/admin",
        };

        var exception = Assert.Throws<InvalidOperationException>(
            options.Validate);

        Assert.Contains(
            nameof(HelloUiPages.Account),
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("https://example.test/admin")]
    [InlineData("//example.test/admin")]
    [InlineData("/\\example.test/admin")]
    [InlineData("/admin?return=1")]
    [InlineData("/admin#fragment")]
    [InlineData("/admin/../login")]
    [InlineData("/admin%2flogin")]
    public void ValidateRejectsUnsafeAuthenticatedRedirectPath(
        string redirectPath)
    {
        var options = new SkopkaHelloUiOptions
        {
            EnabledPages = HelloUiPages.Login,
            AuthenticatedRedirectPath = redirectPath,
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void ServiceRegistrationRejectsUnsafeAuthenticatedRedirectPath()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(
            () => services.AddSkopkaHelloUi(options =>
            {
                options.EnabledPages = HelloUiPages.Login;
                options.AuthenticatedRedirectPath =
                    "https://example.test/admin";
            }));
    }

    [Fact]
    public void RegistrationRequiresOneVisibleLoginIdentifier()
    {
        var options = new SkopkaHelloUiOptions();
        options.Registration.Email =
            HelloUiRegistrationFieldMode.Hidden;
        options.Registration.UserName =
            HelloUiRegistrationFieldMode.Hidden;
        options.Registration.Phone =
            HelloUiRegistrationFieldMode.Hidden;

        var exception = Assert.Throws<InvalidOperationException>(
            options.Validate);

        Assert.Contains("login handle", exception.Message);
    }

    [Fact]
    public void RegistrationRejectsUnsupportedFieldMode()
    {
        var options = new SkopkaHelloUiOptions();
        options.Registration.Email =
            (HelloUiRegistrationFieldMode)42;

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData("home")]
    [InlineData("http://example.test/")]
    [InlineData("https://user:password@example.test/")]
    [InlineData("https://example.test/?source=account")]
    [InlineData("https://example.test/#account")]
    [InlineData("https://example.test/../account")]
    [InlineData("javascript:alert(1)")]
    public void ValidateRejectsUnsafeApplicationHomeUrl(string homeUrl)
    {
        var options = new SkopkaHelloUiOptions
        {
            ApplicationHomeUrl = homeUrl,
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData("/app")]
    [InlineData("https://home.example.test/")]
    [InlineData("https://home.example.test:8443/account")]
    public void ValidateAllowsSafeApplicationHomeUrl(string homeUrl)
    {
        var options = new SkopkaHelloUiOptions
        {
            ApplicationHomeUrl = homeUrl,
        };

        options.Validate();

        Assert.Equal(homeUrl, options.ApplicationHomeUrl);
    }

    [Theory]
    [InlineData("terms", true)]
    [InlineData("http://example.test/terms", true)]
    [InlineData("javascript:alert(1)", true)]
    [InlineData("privacy", false)]
    [InlineData("https://user:password@example.test/privacy", false)]
    [InlineData("https://example.test/privacy?version=1", false)]
    public void ValidateRejectsUnsafeLegalDocumentUrl(
        string documentUrl,
        bool isTermsOfService)
    {
        var options = new SkopkaHelloUiOptions
        {
            TermsOfServiceUrl = isTermsOfService ? documentUrl : null,
            PrivacyPolicyUrl = isTermsOfService ? null : documentUrl,
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData("/terms", "https://legal.example.test/privacy")]
    [InlineData(
        "https://legal.example.test:8443/terms",
        "/privacy")]
    public void ValidateAllowsSafeLegalDocumentUrls(
        string termsOfServiceUrl,
        string privacyPolicyUrl)
    {
        var options = new SkopkaHelloUiOptions
        {
            TermsOfServiceUrl = termsOfServiceUrl,
            PrivacyPolicyUrl = privacyPolicyUrl,
        };

        options.Validate();

        Assert.Equal(termsOfServiceUrl, options.TermsOfServiceUrl);
        Assert.Equal(privacyPolicyUrl, options.PrivacyPolicyUrl);
    }

    [Fact]
    public void LegalDocumentUrlsContributeToSharedConsentPolicy()
    {
        var services = new ServiceCollection();
        services.AddSkopkaHello<TestProfile>();
        services.AddSkopkaHelloUi(options =>
        {
            options.TermsOfServiceUrl = "/terms";
            options.PrivacyPolicyUrl = "/privacy";
        });
        using var provider = services.BuildServiceProvider();

        var policy = provider.GetRequiredService<
            IHelloRegistrationConsentPolicy>();
        var result = policy.Validate(HelloRegistrationConsent.None);

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Equal(
            HelloRegistrationErrors.ConsentRequiredCode,
            error.Code);
        var details = Assert.IsType<ValidationDetails>(error.Details);
        Assert.Equal(2, details.Fields.Count);
    }

    [Fact]
    public void ValidateRejectsAuthenticatedRedirectToLoginPage()
    {
        var options = new SkopkaHelloUiOptions
        {
            EnabledPages = HelloUiPages.Login,
            AuthenticatedRedirectPath = "/identity/login",
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => options.ValidateRoutes(
                new HelloUiRoutePaths("/identity")));

        Assert.Contains(
            nameof(HelloUiPages.Login),
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRejectsRequestPathWithQuery()
    {
        var options = new SkopkaHelloUiOptions
        {
            CustomCssRequestPath = "/custom.css?version=1",
        };

        Assert.Throws<InvalidOperationException>(
            options.Validate);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/{tenant}/custom.css")]
    [InlineData("/assets/{*path}")]
    [InlineData("/assets//custom.css")]
    [InlineData("/assets/../custom.css")]
    public void ValidateRejectsNonLiteralOrAmbiguousRequestPath(
        string requestPath)
    {
        var options = new SkopkaHelloUiOptions
        {
            CustomCssRequestPath = requestPath,
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void ValidateAllowsDisabledCustomCss()
    {
        var options = new SkopkaHelloUiOptions();

        options.Validate();

        Assert.Null(options.CustomCssFilePath);
        Assert.Equal(
            "/_content/Skopka.Hello.UI/custom.css",
            options.CustomCssRequestPath);
    }

    [Fact]
    public void NoticeIsDisabledByDefault()
    {
        var options = new SkopkaHelloUiOptions();

        options.Validate();

        Assert.Null(options.NoticeText);
    }

    [Fact]
    public void ValidateRejectsInsecureHostAuthenticationCookie()
    {
        var options = new SkopkaHelloUiOptions
        {
            SecureCookies = false,
        };

        var exception = Assert.Throws<InvalidOperationException>(
            options.Validate);

        Assert.Contains(
            "__Host-",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateAllowsBuiltInStylesToBeDisabled()
    {
        var options = new SkopkaHelloUiOptions
        {
            BuiltInStylesEnabled = false,
        };

        options.Validate();

        Assert.False(options.BuiltInStylesEnabled);
    }

    [Fact]
    public async Task EndpointServesOnlyConfiguredCssFile()
    {
        var temporaryFile = Path.Combine(
            Path.GetTempPath(),
            $"skopka-hello-{Guid.NewGuid():N}.css");
        await File.WriteAllTextAsync(
            temporaryFile,
            ":root { --test-color: rebeccapurple; }",
            Encoding.UTF8);

        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddSkopkaHelloUi(options =>
                options.CustomCssFilePath = temporaryFile);
            await using var application = builder.Build();
            application.MapSkopkaHelloCustomCss();

            var routeBuilder = (IEndpointRouteBuilder)application;
            var endpoint = routeBuilder.DataSources
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .Single(candidate =>
                    string.Equals(
                        candidate.RoutePattern.RawText,
                        SkopkaHelloUiOptions
                            .DefaultCustomCssRequestPath,
                        StringComparison.Ordinal));
            var responseBody = new MemoryStream();
            var httpContext = new DefaultHttpContext
            {
                RequestServices = application.Services,
            };
            httpContext.Response.Body = responseBody;

            await endpoint.RequestDelegate!(httpContext);

            Assert.Equal(
                StatusCodes.Status200OK,
                httpContext.Response.StatusCode);
            Assert.Equal(
                "text/css; charset=utf-8",
                httpContext.Response.ContentType);
            Assert.Equal(
                "no-cache",
                httpContext.Response.Headers.CacheControl);
            responseBody.Position = 0;
            using var reader = new StreamReader(
                responseBody,
                Encoding.UTF8);
            var content = await reader.ReadToEndAsync();
            Assert.Contains(
                "--test-color",
                content,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(temporaryFile);
        }
    }

    [Fact]
    public async Task EndpointRejectsConfiguredUiRouteCollision()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(
            new HelloUiRoutePaths("/portal"));
        builder.Services.AddSkopkaHelloUi(options =>
            options.CustomCssRequestPath = "/portal/login");
        await using var application = builder.Build();

        var exception = Assert.Throws<InvalidOperationException>(
            application.MapSkopkaHelloCustomCss);

        Assert.Contains(
            "collides",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EndpointRejectsExistingHostRouteCollision()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSkopkaHelloUi(options =>
            options.CustomCssRequestPath = "/host-info");
        await using var application = builder.Build();
        application.MapGet("/host-info", () => "host");

        var exception = Assert.Throws<InvalidOperationException>(
            application.MapSkopkaHelloCustomCss);

        Assert.Contains(
            "collides",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record TestProfile(string DisplayName);
}
