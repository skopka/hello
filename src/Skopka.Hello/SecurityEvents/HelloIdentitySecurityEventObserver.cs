using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Skopka.Identity.SecurityEvents;

namespace Skopka.Hello;

internal sealed partial class HelloIdentitySecurityEventObserver(
    IHttpContextAccessor httpContextAccessor,
    IHelloSecurityEventSink sink,
    ILogger<HelloIdentitySecurityEventObserver>? logger = null)
    : IIdentitySecurityEventObserver
{
    public void OnEvent(IdentitySecurityEvent securityEvent)
    {
        ArgumentNullException.ThrowIfNull(securityEvent);

        try
        {
            var httpContext = httpContextAccessor.HttpContext;
            var result = sink.Write(
                new HelloSecurityEventEnvelope(
                    securityEvent.EventId,
                    securityEvent.Type,
                    securityEvent.UserId,
                    ReadActorId(httpContext?.User),
                    securityEvent.ResourceId,
                    httpContext?.TraceIdentifier,
                    securityEvent.OccurredAt,
                    new Dictionary<string, string>()));
            if (!result.IsSuccess && logger is not null)
            {
                SecurityEventSinkFailed(
                    logger,
                    securityEvent.EventId,
                    securityEvent.Type,
                    result.Errors.FirstOrDefault()?.Code
                        ?? HelloAuditErrorCodes.Failed,
                    null);
            }
        }
        catch (Exception exception)
        {
            // Identity observers are post-commit and must never break the operation.
            if (logger is not null)
            {
                SecurityEventSinkFailed(
                    logger,
                    securityEvent.EventId,
                    securityEvent.Type,
                    HelloAuditErrorCodes.Failed,
                    exception);
            }
        }
    }

    private static Guid? ReadActorId(ClaimsPrincipal? principal)
    {
        var subject = principal?.FindFirstValue("sub")
            ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(subject, out var actorId)
            ? actorId
            : null;
    }

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Error,
        Message = "Security-event sink failed for event {eventId}; type: {eventType}; error code: {errorCode}.")]
    private static partial void SecurityEventSinkFailed(
        ILogger logger,
        Guid eventId,
        string eventType,
        string errorCode,
        Exception? exception);
}
