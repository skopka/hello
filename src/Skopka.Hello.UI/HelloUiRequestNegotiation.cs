using Microsoft.AspNetCore.Http;

namespace Skopka.Hello.UI;

internal static class HelloUiRequestNegotiation
{
    public static bool PrefersHtml(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (String.Equals(
                context.Request.Headers["Sec-Fetch-Mode"],
                "navigate",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var accepted = context.Request.GetTypedHeaders().Accept;
        if (accepted is null || accepted.Count == 0)
        {
            return false;
        }

        double htmlQuality = 0;
        double jsonQuality = 0;
        foreach (var mediaType in accepted)
        {
            var quality = mediaType.Quality ?? 1;
            var value = mediaType.MediaType.Value;
            if (String.Equals(
                    value,
                    "text/html",
                    StringComparison.OrdinalIgnoreCase)
                || String.Equals(
                    value,
                    "application/xhtml+xml",
                    StringComparison.OrdinalIgnoreCase))
            {
                htmlQuality = Math.Max(htmlQuality, quality);
            }
            else if (String.Equals(
                         value,
                         "application/json",
                         StringComparison.OrdinalIgnoreCase)
                     || String.Equals(
                         value,
                         "application/problem+json",
                         StringComparison.OrdinalIgnoreCase))
            {
                jsonQuality = Math.Max(jsonQuality, quality);
            }
        }

        return htmlQuality > 0 && htmlQuality >= jsonQuality;
    }
}
