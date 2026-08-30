using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Skopka.Hello.UI.Pages;

public sealed class ErrorModel : PageModel
{
    public int ErrorStatusCode { get; private set; }

    public string TitleKey { get; private set; } = "Error.Title.500";

    public string DetailKey { get; private set; } = "Error.Detail.500";

    public string? RequestId { get; private set; }

    public void OnGet(int? statusCode)
    {
        var statusCodePages = HttpContext.Features
            .Get<IStatusCodePagesFeature>();
        if (statusCodePages is not null)
        {
            statusCodePages.Enabled = false;
        }

        ErrorStatusCode = NormalizeStatusCode(
            statusCode ?? Response.StatusCode);
        Response.StatusCode = ErrorStatusCode;
        Response.Headers.CacheControl = "no-store, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";

        (TitleKey, DetailKey) = ErrorStatusCode switch
        {
            StatusCodes.Status400BadRequest =>
                ("Error.Title.400", "Error.Detail.400"),
            StatusCodes.Status401Unauthorized =>
                ("Error.Title.401", "Error.Detail.401"),
            StatusCodes.Status403Forbidden =>
                ("Error.Title.403", "Error.Detail.403"),
            StatusCodes.Status404NotFound =>
                ("Error.Title.404", "Error.Detail.404"),
            StatusCodes.Status409Conflict =>
                ("Error.Title.409", "Error.Detail.409"),
            StatusCodes.Status429TooManyRequests =>
                ("Error.Title.429", "Error.Detail.429"),
            _ => ("Error.Title.500", "Error.Detail.500"),
        };

        RequestId = Activity.Current?.Id
            ?? HttpContext.TraceIdentifier;

    }

    private static int NormalizeStatusCode(int statusCode)
        => statusCode is >= 400 and <= 599
            ? statusCode
            : StatusCodes.Status500InternalServerError;
}
