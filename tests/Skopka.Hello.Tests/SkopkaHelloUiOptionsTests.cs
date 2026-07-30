using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Skopka.Hello.UI;

namespace Skopka.Hello.Tests;

public sealed class SkopkaHelloUiOptionsTests
{
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
}
