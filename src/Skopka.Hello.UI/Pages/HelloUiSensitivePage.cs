using Microsoft.AspNetCore.Http;

namespace Skopka.Hello.UI.Pages;

internal static class HelloUiSensitivePage
{
    public static void ApplyResponseHeaders(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Headers.CacheControl = "no-store, max-age=0";
        response.Headers.Pragma = "no-cache";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    }
}
