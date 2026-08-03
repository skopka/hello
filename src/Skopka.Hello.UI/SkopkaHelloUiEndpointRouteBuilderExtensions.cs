using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Skopka.Hello.UI;

public static class SkopkaHelloUiEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapSkopkaHelloUi(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapRazorPages();
        endpoints.MapSkopkaHelloCustomCss();
        return endpoints;
    }

    public static RouteHandlerBuilder MapSkopkaHelloCustomCss(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider
            .GetRequiredService<SkopkaHelloUiOptions>();
        options.ValidateRouteCollisions(
            endpoints.ServiceProvider.GetService<HelloUiRoutePaths>());
        if (HasRouteCollision(
                endpoints,
                options.CustomCssRequestPath))
        {
            throw new InvalidOperationException(
                "The custom CSS request path collides with an existing endpoint.");
        }

        return endpoints.MapGet(
                options.CustomCssRequestPath,
                (HttpContext httpContext) =>
                    ServeCustomCss(options, httpContext))
            .AllowAnonymous()
            .WithName("SkopkaHelloCustomCss");
    }

    private static bool HasRouteCollision(
        IEndpointRouteBuilder endpoints,
        string requestPath)
        => endpoints.DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Any(endpoint => string.Equals(
                "/" + (endpoint.RoutePattern.RawText ?? string.Empty)
                    .Trim('/'),
                requestPath,
                StringComparison.OrdinalIgnoreCase));

    private static IResult ServeCustomCss(
        SkopkaHelloUiOptions options,
        HttpContext httpContext)
    {
        if (options.CustomCssFilePath is null)
        {
            return TypedResults.NotFound();
        }

        var file = new FileInfo(
            Path.GetFullPath(options.CustomCssFilePath));
        file.Refresh();
        if (!file.Exists)
        {
            return TypedResults.NotFound();
        }

        FileStream stream;
        try
        {
            stream = new FileStream(
                file.FullName,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.ReadWrite | FileShare.Delete,
                    Options = FileOptions.Asynchronous
                        | FileOptions.SequentialScan,
                });
        }
        catch (FileNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch (DirectoryNotFoundException)
        {
            return TypedResults.NotFound();
        }

        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers["X-Content-Type-Options"] =
            "nosniff";

        return Results.Stream(
            stream,
            "text/css; charset=utf-8",
            lastModified: file.LastWriteTimeUtc,
            enableRangeProcessing: false);
    }
}
