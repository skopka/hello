using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Skopka.Hello.Oidc;

namespace Skopka.Hello.Tests;

public sealed class HelloOidcOptionsTests
{
    [Theory]
    [InlineData("-github")]
    [InlineData("github/enterprise")]
    [InlineData("гитхаб")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void RegistrationRejectsUnsafeProviderId(string providerId)
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            AddOidc(services, options =>
                options.Providers[providerId] = CreateProvider()));

        Assert.Contains(
            "provider id",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegistrationRejectsProviderIdsWithSameNormalizedCallback()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            AddOidc(services, options =>
            {
                options.Providers["github"] = CreateProvider();
                options.Providers[" GITHUB "] = CreateProvider(
                    displayName: "GitHub duplicate");
            }));

        Assert.Contains(
            "configured more than once",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RegistrationRejectsHttpAuthority()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            AddOidc(services, options =>
                options.Providers["github"] = CreateProvider(
                    authority: "http://accounts.example.test")));

        Assert.Contains(
            "HTTPS authority",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RegistrationRejectsDisabledHttpsMetadataValidation()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            AddOidc(services, options =>
            {
                var provider = CreateProvider();
                provider.RequireHttpsMetadata = false;
                options.Providers["github"] = provider;
            }));

        Assert.Contains(
            "HTTPS metadata validation",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RegistrationRejectsEnabledProviderWithoutSecureCookies()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddSkopkaHelloOidc<object>(options =>
            {
                options.PublicOrigin = new Uri(
                    "https://hello.example.test");
                options.SecureCookies = false;
                options.ExternalCookieName =
                    "Skopka.Hello.External";
                options.PendingCookieName =
                    "Skopka.Hello.External.Pending";
                options.LinkRequestCookieName =
                    "Skopka.Hello.External.LinkRequest";
                options.Providers["github"] = CreateProvider();
            }));

        Assert.Contains(
            "require SecureCookies",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RegistrationRejectsEnabledProviderWithHttpPublicOrigin()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddSkopkaHelloOidc<object>(options =>
            {
                options.PublicOrigin = new Uri(
                    "http://hello.example.test");
                options.Providers["github"] = CreateProvider();
            }));

        Assert.Contains(
            "HTTPS PublicOrigin",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task NamedOptionsUseSecureCodeFlowAndKeepBearerDefaults()
    {
        const string bearerScheme = "Skopka.Test.Bearer";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = bearerScheme;
            options.DefaultChallengeScheme = bearerScheme;
        });
        AddOidc(services, options =>
        {
            var github = CreateProvider(
                authority: "https://github.example.test/");
            github.Scopes.Add("groups");
            options.Providers["GitHub"] = github;
            options.Providers["contoso"] = CreateProvider(
                displayName: "Contoso",
                authority: "https://login.contoso.test");
        });

        using var provider = services.BuildServiceProvider();
        var authentication = provider
            .GetRequiredService<IOptions<AuthenticationOptions>>()
            .Value;
        Assert.Equal(
            bearerScheme,
            authentication.DefaultAuthenticateScheme);
        Assert.Equal(
            bearerScheme,
            authentication.DefaultChallengeScheme);

        var githubScheme = HelloOidcDefaults.ProviderSchemePrefix
            + "github";
        var contosoScheme = HelloOidcDefaults.ProviderSchemePrefix
            + "contoso";
        var schemes = provider.GetRequiredService<
            IAuthenticationSchemeProvider>();
        Assert.NotNull(await schemes.GetSchemeAsync(githubScheme));
        Assert.NotNull(await schemes.GetSchemeAsync(contosoScheme));

        var oidcOptions = provider.GetRequiredService<
            IOptionsMonitor<OpenIdConnectOptions>>();
        var github = oidcOptions.Get(githubScheme);
        var contoso = oidcOptions.Get(contosoScheme);

        Assert.Equal(
            HelloOidcDefaults.ExternalCookieScheme,
            github.SignInScheme);
        Assert.Equal(
            "https://github.example.test",
            github.Authority);
        Assert.Equal(
            new PathString(
                HelloOidcDefaults.CallbackPathPrefix + "github"),
            github.CallbackPath);
        Assert.NotEqual(github.CallbackPath, contoso.CallbackPath);
        Assert.Equal(OpenIdConnectResponseType.Code, github.ResponseType);
        Assert.True(github.UsePkce);
        Assert.False(github.SaveTokens);
        Assert.False(github.MapInboundClaims);
        Assert.False(github.GetClaimsFromUserInfoEndpoint);
        Assert.True(github.RequireHttpsMetadata);
        Assert.Contains("openid", github.Scope);
        Assert.Contains("profile", github.Scope);
        Assert.Contains("email", github.Scope);
        Assert.Contains("groups", github.Scope);
        Assert.True(github.ProtocolValidator.RequireNonce);
        Assert.NotNull(github.StateDataFormat);
        Assert.True(github.TokenValidationParameters.ValidateIssuer);
        Assert.True(github.TokenValidationParameters.ValidateAudience);
        Assert.True(github.TokenValidationParameters.ValidateLifetime);
        Assert.True(
            github.TokenValidationParameters.ValidateIssuerSigningKey);
        Assert.True(github.CorrelationCookie.HttpOnly);
        Assert.True(github.CorrelationCookie.IsEssential);
        Assert.Equal(
            SameSiteMode.None,
            github.CorrelationCookie.SameSite);
        Assert.Equal(
            CookieSecurePolicy.Always,
            github.CorrelationCookie.SecurePolicy);
        Assert.True(github.NonceCookie.HttpOnly);
        Assert.True(github.NonceCookie.IsEssential);
        Assert.Equal(SameSiteMode.None, github.NonceCookie.SameSite);
        Assert.Equal(
            CookieSecurePolicy.Always,
            github.NonceCookie.SecurePolicy);

        var cookieOptions = provider.GetRequiredService<
            IOptionsMonitor<CookieAuthenticationOptions>>();
        var external = cookieOptions.Get(
            HelloOidcDefaults.ExternalCookieScheme);
        var pending = cookieOptions.Get(
            HelloOidcDefaults.PendingCookieScheme);
        var linkRequest = cookieOptions.Get(
            HelloOidcDefaults.LinkRequestCookieScheme);
        Assert.True(external.Cookie.HttpOnly);
        Assert.Equal(
            CookieSecurePolicy.Always,
            external.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Lax, external.Cookie.SameSite);
        Assert.False(external.SlidingExpiration);
        Assert.True(pending.Cookie.HttpOnly);
        Assert.Equal(
            CookieSecurePolicy.Always,
            pending.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Strict, pending.Cookie.SameSite);
        Assert.False(pending.SlidingExpiration);
        Assert.True(linkRequest.Cookie.HttpOnly);
        Assert.Equal(
            CookieSecurePolicy.Always,
            linkRequest.Cookie.SecurePolicy);
        Assert.Equal(
            SameSiteMode.Strict,
            linkRequest.Cookie.SameSite);
        Assert.False(linkRequest.SlidingExpiration);
    }

    private static void AddOidc(
        IServiceCollection services,
        Action<HelloOidcOptions> configureProviders)
        => services.AddSkopkaHelloOidc<object>(options =>
        {
            options.PublicOrigin = new Uri(
                "https://hello.example.test");
            configureProviders(options);
        });

    private static HelloOidcProviderOptions CreateProvider(
        string displayName = "GitHub",
        string authority = "https://accounts.example.test")
        => new()
        {
            DisplayName = displayName,
            Authority = authority,
            ClientId = "hello-tests",
            ClientSecret = "not-a-production-secret",
        };
}
