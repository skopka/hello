using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Localization;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;
using Skopka.Identity.RateLimiting;

namespace Skopka.Hello.Endpoints;

public static class OperationResultProblemMapper
{
    public static ProblemMapping Map(
        IReadOnlyCollection<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var primary = errors.FirstOrDefault()
            ?? new Error(
                "hello.operation.failed",
                "The operation failed.",
                ErrorType.Failure);
        var status = GetStatus(primary);
        var validationErrors = errors
            .Select(error => error.Details)
            .OfType<ValidationDetails>()
            .SelectMany(details => details.Fields)
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .SelectMany(pair => pair.Value)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        var retryAfter = primary.Details is RateLimitDetails rateLimit
            ? rateLimit.RetryAfter
            : null;

        return new ProblemMapping(
            status,
            GetTitle(status),
            primary.Message,
            primary.Code,
            validationErrors,
            retryAfter);
    }

    public static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult ToResult(
        OperationResult result,
        HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(httpContext);

        return ToResult(result.Errors, httpContext);
    }

    public static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult ToResult<T>(
        OperationResult<T> result,
        HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(httpContext);

        return ToResult(result.Errors, httpContext);
    }

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult ToResult(
        IReadOnlyCollection<Error> errors,
        HttpContext httpContext)
    {
        var mapping = LocalizePhoneError(Map(errors), errors, httpContext);
        if (mapping.RetryAfter is { } retryAfter)
        {
            var seconds = Math.Max(
                0,
                (long)Math.Ceiling(
                    (retryAfter - DateTimeOffset.UtcNow).TotalSeconds));
            httpContext.Response.Headers.RetryAfter =
                seconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
        }

        var problem = new ProblemDetails
        {
            Status = mapping.Status,
            Title = mapping.Title,
            Detail = mapping.Detail,
            Type = $"urn:skopka:problem:{mapping.Code}",
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["code"] = mapping.Code;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        if (mapping.ValidationErrors.Count > 0)
        {
            problem.Extensions["errors"] = mapping.ValidationErrors;
        }

        return TypedResults.Problem(problem);
    }

    private static ProblemMapping LocalizePhoneError(
        ProblemMapping mapping, IReadOnlyCollection<Error> errors, HttpContext context)
    {
        if (mapping.Code is not IdentityErrorCodes.PhoneInvalidFormat
            and not IdentityErrorCodes.PhoneCountryNotAllowed) return mapping;

        var culture = context.Features.Get<IRequestCultureFeature>()?.RequestCulture.UICulture.Name;
        if (culture is null)
        {
            try
            {
                culture = context.Request.GetTypedHeaders().AcceptLanguage?
                    .OrderByDescending(value => value.Quality ?? 1)
                    .Where(value => (value.Quality ?? 1) > 0)
                    .Select(value => value.Value.ToString())
                    .FirstOrDefault(value => IsLanguage(value, "ru") || IsLanguage(value, "en"));
            }
            catch (FormatException)
            {
                // Malformed negotiation headers do not change the operation result.
            }
        }
        culture ??= System.Globalization.CultureInfo.CurrentUICulture.Name;
        var russian = IsLanguage(culture, "ru");
        var codes = (errors.FirstOrDefault()?.Details as ValidationDetails)?.AllowedPhoneCountryCodes;
        var message = mapping.Code == IdentityErrorCodes.PhoneInvalidFormat
            ? (russian ? "Введите корректный номер телефона." : "Enter a valid phone number.")
            : codes is { Count: > 0 }
                ? (russian ? $"Допускаются только номера с кодом {string.Join(", ", codes)}."
                    : $"Only phone numbers with the code {string.Join(", ", codes)} are allowed.")
                : (russian ? "Этот код страны не разрешён." : "This phone country code is not allowed.");
        var fields = new Dictionary<string, string[]>(mapping.ValidationErrors, StringComparer.Ordinal);
        if (fields.ContainsKey("phone")) fields["phone"] = [message];
        return mapping with { Detail = message, ValidationErrors = fields };
    }

    private static bool IsLanguage(string value, string language)
        => value.Equals(language, StringComparison.OrdinalIgnoreCase)
            || value.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase);

    private static int GetStatus(Error error)
    {
        if (string.Equals(
                error.Code,
                IdentityErrorCodes.RateLimitExceeded,
                StringComparison.Ordinal)
            || string.Equals(
                error.Code,
                HelloDeliveryErrorCodes.QueueFull,
                StringComparison.Ordinal))
        {
            return StatusCodes.Status429TooManyRequests;
        }

        if (string.Equals(
                error.Code,
                HelloDeliveryErrorCodes.NotConfigured,
                StringComparison.Ordinal)
            || string.Equals(
                error.Code,
                HelloDeliveryErrorCodes.Failed,
                StringComparison.Ordinal))
        {
            return StatusCodes.Status503ServiceUnavailable;
        }

        return error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
    }

    private static string GetTitle(int status)
        => status switch
        {
            StatusCodes.Status400BadRequest => "Bad Request",
            StatusCodes.Status401Unauthorized => "Unauthorized",
            StatusCodes.Status403Forbidden => "Forbidden",
            StatusCodes.Status404NotFound => "Not Found",
            StatusCodes.Status409Conflict => "Conflict",
            StatusCodes.Status429TooManyRequests => "Too Many Requests",
            StatusCodes.Status503ServiceUnavailable =>
                "Service Unavailable",
            _ => "Operation Failed",
        };
}

public sealed record ProblemMapping(
    int Status,
    string Title,
    string Detail,
    string Code,
    IReadOnlyDictionary<string, string[]> ValidationErrors,
    DateTimeOffset? RetryAfter);
