using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Antiforgery;
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
        MapSkopkaHelloCulture(endpoints);
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

    private static void MapSkopkaHelloCulture(
        IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider
            .GetRequiredService<SkopkaHelloUiOptions>();
        if (!options.Localization.Enabled)
        {
            return;
        }

        var routes = endpoints.ServiceProvider
            .GetRequiredService<HelloUiRoutePaths>();
        if (HasRouteCollision(endpoints, routes.CulturePath))
        {
            throw new InvalidOperationException(
                "The UI culture path collides with an existing endpoint.");
        }

        endpoints.MapPost(
                routes.CulturePath,
                ChangeCultureAsync)
            .AllowAnonymous()
            .WithName("SkopkaHelloCulture");
    }

    private static async Task<IResult> ChangeCultureAsync(
        HttpContext httpContext,
        IAntiforgery antiforgery,
        SkopkaHelloUiOptions options,
        HelloUiRoutePaths routes,
        CancellationToken cancellationToken)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return TypedResults.BadRequest();
        }

        var form = await httpContext.Request.ReadFormAsync(
            cancellationToken);
        var requestedCulture = form["culture"].ToString();
        if (!options.Localization.TryGetSupportedCulture(
                requestedCulture,
                out var culture))
        {
            return TypedResults.BadRequest();
        }

        httpContext.Response.Cookies.Append(
            options.Localization.CultureCookieName,
            culture.Name,
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                MaxAge = TimeSpan.FromDays(365),
                Path = "/",
                SameSite = options.CookieSameSite,
                Secure = options.SecureCookies,
            });

        var returnUrl = form["returnUrl"].ToString();
        return TypedResults.LocalRedirect(
            IsSafeLocalUrl(returnUrl)
                ? returnUrl
                : routes.RootPath);
    }

    private static bool IsSafeLocalUrl(string? url)
        => !String.IsNullOrEmpty(url)
            && url[0] == '/'
            && (url.Length == 1
                || url[1] is not '/' and not '\\')
            && !url.Contains('\\')
            && !url.Any(character => Char.IsControl(character));

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
