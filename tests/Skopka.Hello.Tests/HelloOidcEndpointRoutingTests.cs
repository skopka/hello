using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Hello.Endpoints;
using Skopka.Hello.Oidc;

namespace Skopka.Hello.Tests;

public sealed class HelloOidcEndpointRoutingTests
{
    [Fact]
    public async Task OidcConfigurationPublishesHeadlessBrowserFlow()
    {
        await using var application = CreateApplication(
            oidcEnabled: true,
            selfRegistrationEnabled: true);

        var routes = GetRoutes(application);

        Assert.Contains(
            new MappedRoute(
                HttpMethods.Get,
                "/auth/external/{providerId}/challenge"),
            routes);
        Assert.Contains(
            new MappedRoute(
                HttpMethods.Post,
                HelloOidcDefaults.ApiCompletionPath),
            routes);
        Assert.Contains(
            new MappedRoute(
                HttpMethods.Get,
                HelloOidcDefaults.ApiRegistrationPath),
            routes);
        Assert.Contains(
            new MappedRoute(
                HttpMethods.Post,
                HelloOidcDefaults.ApiRegistrationPath),
            routes);
        Assert.Contains(
            new MappedRoute(
                HttpMethods.Delete,
                "/auth/external/flow"),
            routes);
        Assert.Contains(
            new MappedRoute(
                HttpMethods.Get,
                HelloOidcDefaults.ApiLinkChallengePath),
            routes);
        Assert.Contains(
            new MappedRoute(
                HttpMethods.Post,
                "/account/external-logins/{providerId}/link"),
            routes);
        Assert.Contains(
            new MappedRoute(
                HttpMethods.Post,
                "/account/external-logins/link/challenge"),
            routes);
        Assert.Contains(
            new MappedRoute(
                HttpMethods.Put,
                "/account/external-logins/link"),
            routes);
        Assert.Contains(
            new MappedRoute(
                HttpMethods.Post,
                "/account/external-logins/{providerId}/unlink/challenge"),
            routes);
        Assert.Contains(
            new MappedRoute(
                HttpMethods.Delete,
                "/account/external-logins/unlink"),
            routes);
    }

    [Fact]
    public async Task DisabledSelfRegistrationRemovesExternalRegistrationApi()
    {
        await using var application = CreateApplication(
            oidcEnabled: true,
            selfRegistrationEnabled: false);

        var routes = GetRoutes(application);

        Assert.DoesNotContain(
            routes,
            route => route.Path == HelloOidcDefaults.ApiRegistrationPath);
        Assert.Contains(
            new MappedRoute(
                HttpMethods.Post,
                HelloOidcDefaults.ApiCompletionPath),
            routes);
        Assert.Contains(
            new MappedRoute(
                HttpMethods.Delete,
                "/auth/external/flow"),
            routes);
    }

    [Fact]
    public async Task MissingOidcConfigurationPublishesNoHeadlessOidcRoutes()
    {
        await using var application = CreateApplication(
            oidcEnabled: false,
            selfRegistrationEnabled: true);

        var routes = GetRoutes(application);

        Assert.DoesNotContain(
            routes,
            route => route.Path.StartsWith(
                HelloOidcDefaults.ApiPathPrefix,
                StringComparison.Ordinal));
    }

    private static WebApplication CreateApplication(
        bool oidcEnabled,
        bool selfRegistrationEnabled)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSkopkaHello<TestProfile>(options =>
            options.SelfRegistrationEnabled =
                selfRegistrationEnabled);
        if (oidcEnabled)
        {
            builder.Services.AddSkopkaHelloOidc<TestProfile>(options =>
            {
                options.PublicOrigin = new Uri("https://hello.test/");
                options.Providers["test"] =
                    new HelloOidcProviderOptions
                    {
                        DisplayName = "Test provider",
                        Authority = "https://provider.test/",
                        ClientId = "test-client",
                        ClientSecret = "test-client-secret",
                    };
            });
        }

        var application = builder.Build();
        application.MapSkopkaHello<TestProfile>();
        return application;
    }

    private static HashSet<MappedRoute> GetRoutes(
        WebApplication application)
        => ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
            {
                var path = "/" + (endpoint.RoutePattern.RawText
                    ?? string.Empty).TrimStart('/');
                return endpoint.Metadata
                    .GetMetadata<HttpMethodMetadata>()?
                    .HttpMethods
                    .Select(method => new MappedRoute(method, path))
                    ?? [];
            })
            .ToHashSet();

    private sealed record MappedRoute(string Method, string Path);

    private sealed record TestProfile(string DisplayName);
}
