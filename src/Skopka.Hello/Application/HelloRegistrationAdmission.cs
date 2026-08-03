using Microsoft.AspNetCore.Http;
using Skopka.Abstraction.OperationResult;
using Skopka.Identity.Errors;
using Skopka.Identity.RateLimiting;

namespace Skopka.Hello;

public enum HelloRegistrationKind
{
    Password = 0,
    External = 1,
}

public sealed record HelloRegistrationAdmissionContext(
    HelloRegistrationKind Kind,
    string? ClientKey);

public interface IHelloRegistrationAdmissionPolicy
{
    Task<OperationResult> CheckAsync(
        HelloRegistrationAdmissionContext context,
        CancellationToken cancellationToken);
}

internal sealed class HelloRegistrationAdmission<TProfile>(
    IEnumerable<IHelloRegistrationAdmissionPolicy> policies,
    IEnumerable<IIdentityRateLimiter<TProfile>> rateLimiters,
    IHttpContextAccessor httpContextAccessor,
    IHelloRequestContext requestContext,
    SkopkaHelloOptions options)
{
    private const string ClientScope = "hello.registration.client";
    private const string GlobalScope = "hello.registration.global";
    private const string GlobalKey = "all";
    private const string UnavailableClientKey = "unavailable";

    private readonly IIdentityRateLimiter<TProfile>? rateLimiter =
        rateLimiters.FirstOrDefault();

    public async Task<OperationResult> CheckAsync(
        HelloRegistrationKind kind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var clientKey = httpContextAccessor.HttpContext is { } httpContext
            ? requestContext.CreateClientKey(httpContext)
            : null;
        var context = new HelloRegistrationAdmissionContext(
            kind,
            clientKey);
        foreach (var policy in policies)
        {
            var result = await policy.CheckAsync(
                context,
                cancellationToken);
            if (!result.IsSuccess)
            {
                return result;
            }
        }

        if (rateLimiter is null)
        {
            return OperationResultFactory.Success();
        }

        var clientDecision = await rateLimiter.HitAsync(
            new RateLimitRequest(
                ClientScope,
                NormalizeClientKey(clientKey),
                options.RegistrationClientPermitLimit,
                options.RegistrationClientWindow),
            cancellationToken);
        if (!clientDecision.IsAllowed)
        {
            return Exceeded(clientDecision.RetryAfter);
        }

        var globalDecision = await rateLimiter.HitAsync(
            new RateLimitRequest(
                GlobalScope,
                GlobalKey,
                options.RegistrationGlobalPermitLimit,
                options.RegistrationGlobalWindow),
            cancellationToken);
        return globalDecision.IsAllowed
            ? OperationResultFactory.Success()
            : Exceeded(globalDecision.RetryAfter);
    }

    private static string NormalizeClientKey(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return UnavailableClientKey;
        }

        return normalized.Length <= RateLimitLimits.MaximumClientKeyLength
            ? normalized
            : normalized[..RateLimitLimits.MaximumClientKeyLength];
    }

    private static OperationResult Exceeded(
        DateTimeOffset? retryAfter)
        => OperationResultFactory.Fail(
            new Error(
                IdentityErrorCodes.RateLimitExceeded,
                "Too many requests.",
                ErrorType.Forbidden,
                new RateLimitDetails(retryAfter)));
}
