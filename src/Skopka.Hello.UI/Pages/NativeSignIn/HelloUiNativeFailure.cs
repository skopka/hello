using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;
using Skopka.Identity.RateLimiting;

namespace Skopka.Hello.UI.Pages;

internal static class HelloUiNativeFailure
{
    public static IActionResult Result(HttpContext context, IReadOnlyCollection<Error> errors, IHelloUiLocalizer text)
    {
        var error = errors.FirstOrDefault();
        var limited = error?.Code == IdentityErrorCodes.RateLimitExceeded;
        if (error?.Details is RateLimitDetails { RetryAfter: { } retryAfter })
        {
            var seconds = Math.Max(0, (long)Math.Ceiling((retryAfter - DateTimeOffset.UtcNow).TotalSeconds));
            context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }
        return new ObjectResult(new ProblemDetails
        {
            Status = limited ? StatusCodes.Status429TooManyRequests : StatusCodes.Status400BadRequest,
            Title = limited ? text["NativeSignIn.RateLimited"] : text["NativeSignIn.Invalid"],
        })
        { StatusCode = limited ? StatusCodes.Status429TooManyRequests : StatusCodes.Status400BadRequest };
    }
}
