using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Skopka.Hello.UI;

internal sealed class HelloUiRequestCultureFilter(
    SkopkaHelloUiOptions options)
    : IAsyncResourceFilter
{
    private static readonly AcceptLanguageHeaderRequestCultureProvider
        AcceptLanguageProvider = new();

    public async Task OnResourceExecutionAsync(
        ResourceExecutingContext context,
        ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var selectedCulture = await ResolveCultureAsync(
            context.HttpContext);
        var cultureInfo = CultureInfo.GetCultureInfo(
            selectedCulture.Name);

        try
        {
            CultureInfo.CurrentCulture = cultureInfo;
            CultureInfo.CurrentUICulture = cultureInfo;
            context.HttpContext.Response.Headers.ContentLanguage =
                cultureInfo.Name;
            await next();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    internal async Task<HelloUiCulture> ResolveCultureAsync(
        HttpContext httpContext)
    {
        var localization = options.Localization;
        if (localization.Enabled
            && httpContext.Request.Cookies.TryGetValue(
                localization.CultureCookieName,
                out var cookieCulture)
            && localization.TryGetSupportedCulture(
                cookieCulture,
                out var selected))
        {
            return selected;
        }

        if (localization.Enabled
            && localization.UseAcceptLanguageHeader)
        {
            var headerResult = await AcceptLanguageProvider
                .DetermineProviderCultureResult(httpContext);
            if (headerResult is not null)
            {
                foreach (var requested in headerResult.Cultures)
                {
                    if (localization.TryGetSupportedCulture(
                            requested.Value,
                            out selected))
                    {
                        return selected;
                    }
                }
            }
        }

        _ = localization.TryGetSupportedCulture(
            localization.DefaultCulture,
            out var defaultCulture);
        return defaultCulture;
    }
}
